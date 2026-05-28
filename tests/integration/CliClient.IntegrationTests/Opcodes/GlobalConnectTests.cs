// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// 0x006D GlobalConnect → 0x0070 GlobalAvatarList round-trip against the
/// live proxy on the global server port (3805). This is the integration
/// assertion for Phase K's proxy↔server global UDP control plane
/// (UDP_GLOBAL_SERVER_PORT 3810): the round-trip succeeds iff the proxy
/// can hand a ticket to the server over UDP 3810 (SendTicket → 0x2002
/// TICKET), the server's HandleGlobalOpcode dispatcher
/// (server/src/UDP_Global.cpp:43-76) routes it through ProcessTicketInfo
/// → SendAvatarList, the server's 0x2003 AVATARLIST reply lands back at
/// the proxy's UDPClient receiver, and the proxy serialises a 0x0070
/// frame back to us.
///
/// <para>
/// Before Phase K this plane didn't exist on the Linux build at all —
/// the kyp-era TCP cluster (Connection/ConnectionManager) was deleted
/// in Phase Q and nothing replaced the proxy↔server global handoff
/// until Phase K resurrected it as a dedicated UDP plane. This test
/// is the failure detector for that resurrection.
/// </para>
///
/// <para>
/// Wire layout for 0x006D's payload (matches Win32
/// <c>ClientToGlobalServer.cpp:124</c> and the Linux port at
/// <c>proxy/ClientToServer_linux_stubs.cpp:126-156</c>):
/// <code>
///   [u32 ticket_len_be][char ticket[ticket_len]]
/// </code>
/// The proxy's handler reads the length but only uses
/// <c>&amp;m_RecvBuffer[4]</c> as a NUL-terminated string for
/// <c>strlen(ticket)</c> downstream, so the length is essentially
/// advisory; we still set it correctly for protocol fidelity.
/// </para>
///
/// <para>
/// The reply we wait for is 0x0070, payload of size
/// <c>sizeof(GlobalAvatarList)</c>. We don't decode the body —
/// CliClient.Core has no GlobalAvatarList codec yet (that comes when
/// the workflow that uses it is wired up). Receiving the opcode at all
/// proves the UDP plane round-trip happened.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class GlobalConnectTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public GlobalConnectTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ValidTicket_RoundTripsThroughUdpGlobalPlane_ReturnsAvatarList()
    {
        var account = TestAccounts.For();

        // 30s budget: TLS login + RSA handshake + the proxy's UDP round-trip
        // to the server (sub-second in the happy path; the proxy's
        // WaitForResponse has a multi-second timeout if something is wrong).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Get a real ticket via /AuthLogin (Phase J SSL listener + Phase N+
        // libpqxx VerifyAccountInfo path). The server's ProcessTicketInfo
        // calls strtok on the ticket to extract the username prefix, so the
        // ticket must be the real "username-XXXXX" string the auth server
        // issued — not a fake.
        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password),
            cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.GlobalHost, _server.GlobalPort, cts.Token);

        // Build the GlobalConnect payload: [u32 len_be][ticket bytes][NUL].
        // The NUL is what the proxy's strlen() at SendTicket walks to.
        byte[] ticketBytes = Encoding.ASCII.GetBytes(login.Ticket!);
        byte[] payload = new byte[4 + ticketBytes.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), (uint)ticketBytes.Length);
        ticketBytes.CopyTo(payload, 4);
        payload[^1] = 0;

        var packet = Packet.ForOpcode(
            OpcodeId.Known.GlobalConnect.Value,
            payload);

        await conn.SendAsync(packet, cts.Token);

        // Drain until we see a GlobalAvatarList. The proxy may forward a
        // 0x0075 GLOBAL_ERROR instead if the server rejected the ticket
        // (banned account, account-in-use collision); for the seeded pool
        // the happy path is the AvatarList.
        Packet? reply = null;
        while (true)
        {
            var p = await conn.ReceiveAsync(cts.Token);
            Assert.NotNull(p);
            if (p!.Header.Opcode == OpcodeId.Known.GlobalAvatarList.Value)
            {
                reply = p;
                break;
            }

            // 0x0075 GlobalError surfaces if the server rejected us — let
            // the test fail loudly with the error code rather than time
            // out silently. Layout: [u32 msg_len][be32 (err+7)][msg bytes].
            if (p.Header.Opcode == 0x0075)
            {
                var span = p.Payload.Span;
                int errCode = -1;
                if (span.Length >= 8)
                    errCode = BinaryPrimitives.ReadInt32BigEndian(span.Slice(4, 4)) - 7;
                throw new Xunit.Sdk.XunitException(
                    $"server returned GlobalError code={errCode}; expected AvatarList");
            }

            // Any other opcode arriving on the global channel before
            // AvatarList is unexpected for this exchange. Loop and keep
            // draining — the proxy doesn't push spontaneous global
            // packets on the happy path, but a future Phase-K hello may.
        }

        // The reply is a complete GlobalAvatarList struct. Receiving the
        // opcode proves the UDP global plane round-trip worked end-to-end:
        //   client → proxy(TCP 3805)
        //   proxy → server(UDP 3810, opcode 0x2002 TICKET)
        //   server → proxy(UDP 3810, opcode 0x2003 AVATARLIST)
        //   proxy → client(TCP 3805, opcode 0x0070 GlobalAvatarList)
        Assert.NotNull(reply);

        // Phase K post-migration: GlobalAvatarList wire size is fixed
        // 2042 bytes (5 × 374 AvatarListItem + 4 num_galaxies + 2 × 84
        // Galaxy). Anything smaller means PacketStructures.h has drifted.
        Assert.Equal(GlobalAvatarListCodec.WireSize, reply!.Payload.Length);

        var decoded = (GlobalAvatarList)new GlobalAvatarListCodec()
            .DecodeInbound(reply.Payload.Span);

        // Five fixed slots — even when the account has no avatars the
        // slots are zeroed but present.
        Assert.Equal(5, decoded.Avatars.Length);

        // The seeded test account `cli_test01` has no rows in the
        // `avatars` table, so all five slots should be zero-filled.
        // We assert *that specific shape* — if a future seed change
        // adds avatars this test will fail loudly and force a refresh
        // of the assertion (rather than silently passing on wrong
        // bytes). Account-id/slot are big-endian on the wire so a
        // mis-decoded int would surface as a wildly large value.
        foreach (var slot in decoded.Avatars)
        {
            Assert.Equal(0, slot.Info.AccountIdLsb);
            Assert.Equal(string.Empty, slot.Data.FirstName);
            Assert.Equal(string.Empty, slot.Data.LastName);
        }

        // Galaxy table: BuildAvatarList hard-codes one galaxy entry.
        Assert.True(decoded.Galaxies.Length >= 1,
            "GlobalAvatarList should carry at least one galaxy entry " +
            "(server's AccountManager::BuildAvatarList hard-codes galaxy[0]).");
        var galaxy = decoded.Galaxies[0];
        Assert.False(string.IsNullOrEmpty(galaxy.Name),
            "Galaxy name should be populated from g_Galaxy_Name " +
            "(server-side global, set from config; empty means the wire " +
            "decode walked off into a NUL field).");
        Assert.Equal(1, galaxy.GalaxyId);
        Assert.True(galaxy.MaxPlayers > 0,
            $"MaxPlayers={galaxy.MaxPlayers} — server's MAX_ONLINE_PLAYERS " +
            "is 500; a zero or negative value means the int32_t migration " +
            "didn't take or the field is being read at the wrong offset.");
    }

    /// <summary>
    /// Negative-path counterpart to the happy-path test above. The
    /// <c>cli_test_status0</c> seed account has <c>status = 0</c>
    /// (STRESS_TEST_CLOSED). LinuxAuth issues a ticket regardless
    /// (it doesn't inspect status), so the global UDP plane is what
    /// rejects: <c>server/src/UDP_Global.cpp:ProcessTicketInfo</c>
    /// emits a 0x2004 GLOBAL_ERROR with code 12 back to the proxy,
    /// which forwards it to the client as a 0x0075 GLOBAL_ERROR frame.
    ///
    /// <para>
    /// This test exercises the full error path:
    /// <code>
    ///   client → proxy(TCP 3805)  [0x006D GlobalConnect]
    ///   proxy → server(UDP 3810)  [0x2002 TICKET]
    ///   server → proxy(UDP 3810)  [0x2004 GLOBAL_ERROR err=12]
    ///   proxy → client(TCP 3805)  [0x0075 GLOBAL_ERROR err=12]
    /// </code>
    /// and validates the proxy's <c>g_GlobalErrorMsg[]</c> table is
    /// wide enough (it was previously truncated at 11 entries and
    /// silently dropped codes 12-14 — see
    /// <c>proxy/ClientToServer_linux_stubs.cpp</c>).
    /// </para>
    ///
    /// <para>
    /// Wire layout of the 0x0075 payload (per
    /// <c>ClientToServer_linux_stubs.cpp:GlobalError</c>):
    /// <code>
    ///   [u32 msg_len_le][u32 be32(err + 7)][char msg[msg_len]]
    /// </code>
    /// The +7 offset is a quirk of the kyp wire protocol preserved
    /// verbatim by the Linux port; the client subtracts it back out.
    /// </para>
    /// </summary>
    [Fact]
    public async Task StressTestClosedAccount_GlobalConnect_ReturnsGlobalErrorCode12()
    {
        var account = TestAccounts.StressTestClosed;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // LinuxAuth doesn't read accounts.status so login succeeds
        // even though the account is STRESS_TEST_CLOSED. The rejection
        // happens later, on the global UDP plane.
        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password),
            cts.Token);
        Assert.True(login.Valid,
            $"login should succeed (LinuxAuth ignores status): {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.GlobalHost, _server.GlobalPort, cts.Token);

        byte[] ticketBytes = Encoding.ASCII.GetBytes(login.Ticket!);
        byte[] payload = new byte[4 + ticketBytes.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), (uint)ticketBytes.Length);
        ticketBytes.CopyTo(payload, 4);
        payload[^1] = 0;

        var packet = Packet.ForOpcode(
            OpcodeId.Known.GlobalConnect.Value,
            payload);

        await conn.SendAsync(packet, cts.Token);

        // Drain until we get the 0x0075 GlobalError. If the server
        // mistakenly serves an AvatarList for a status=0 account the
        // assertion below fails loudly — that would mean someone
        // weakened the server's status check (a CLAUDE.md violation).
        Packet? errReply = null;
        while (true)
        {
            var p = await conn.ReceiveAsync(cts.Token);
            Assert.NotNull(p);

            if (p!.Header.Opcode == 0x0075)
            {
                errReply = p;
                break;
            }

            if (p.Header.Opcode == OpcodeId.Known.GlobalAvatarList.Value)
            {
                throw new Xunit.Sdk.XunitException(
                    "server returned GlobalAvatarList for a STRESS_TEST_CLOSED " +
                    "(status=0) account — the server's status check has been " +
                    "weakened. Restore the check at server/src/UDP_Global.cpp " +
                    "ProcessTicketInfo and verify against a real-server capture " +
                    "before reverting this test.");
            }
        }

        Assert.NotNull(errReply);
        var span = errReply!.Payload.Span;
        Assert.True(span.Length >= 8,
            $"GlobalError payload too short ({span.Length} bytes); expected at " +
            "least [u32 msg_len][u32 be(err+7)] = 8 bytes of header.");

        uint msgLen = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0, 4));
        int errCode = BinaryPrimitives.ReadInt32BigEndian(span.Slice(4, 4)) - 7;

        Assert.Equal(12, errCode); // G_ERROR_STRESS_TEST_CLOSED
        Assert.True(msgLen > 0, "GlobalError msg_len must be > 0 (msg follows the header).");
        Assert.True(span.Length >= 8 + msgLen,
            $"GlobalError payload truncated: header says msg_len={msgLen} but only " +
            $"{span.Length - 8} message bytes followed.");

        // Sanity-check the message text matches what the proxy's
        // g_GlobalErrorMsg[12] table holds. If the proxy table was
        // still truncated this assertion would fail because index 12
        // would either be garbage or the connection would have stalled
        // entirely.
        string msg = Encoding.ASCII.GetString(span.Slice(8, (int)msgLen)).TrimEnd('\0');
        Assert.Contains("not currently accepting new logins", msg);
    }

    /// <summary>
    /// Wave 114 sibling byte-exact pin on the same 0x006D GlobalConnect →
    /// 0x0075 GLOBAL_ERROR direct-reply path probed by
    /// <see cref="StressTestClosedAccount_GlobalConnect_ReturnsGlobalErrorCode12"/>,
    /// but pinning the COMPLETE 89-byte reply payload byte-for-byte
    /// instead of only the int32 errCode field and substring text.
    ///
    /// <para>
    /// SEVENTH byte-exact upgrade of a direct-reply assertion in Phase K
    /// (after Waves 108/109/110/111/112/113). FIRST byte-exact wave on
    /// a PROXY-emit path — all six prior byte-exact waves pinned
    /// server-side emits (SendMessageString, SendPriorityMessageString,
    /// SendClientChatEvent). The 0x0075 GLOBAL_ERROR frame is constructed
    /// and sent directly by <c>Connection::GlobalError</c> at
    /// <c>proxy/ClientToServer_linux_stubs.cpp:250-271</c>; the server
    /// emits a <c>0x2004 GLOBAL_ERROR</c> on the internal UDP 3810 plane
    /// (server/src/UDP_Global.cpp), the proxy's UDPClient receives it,
    /// translates the error code via <c>g_GlobalErrorMsg[]</c> and
    /// serialises 0x0075 back to the client over the TCP 3805 wire. FIRST
    /// byte-exact wave on a hybrid LE+BE framing path — the msg_len
    /// header is written in host-byte-order (little-endian on Linux
    /// x86_64) but the error code is written via <c>ntohl()</c> producing
    /// big-endian bytes; this LE/BE interleave is unique to the proxy's
    /// GlobalError path in our suite.
    /// </para>
    ///
    /// <para>
    /// Reply wire layout (mirror of <c>Connection::GlobalError</c> at
    /// proxy/ClientToServer_linux_stubs.cpp:250-271 for Error=12
    /// G_ERROR_STRESS_TEST_CLOSED):
    /// <code>
    ///   [0..4)   uint32 LE    msg_len = 81    (host-byte-order *((int*) p) = strlen(msg))
    ///   [4..8)   uint32 BE    err+7   = 19    (ntohl((uint32_t)(Error + 7)))
    ///   [8..89)  ASCII        msg     = "Sorry, the server is not currently accepting new logins.  Please try again later."
    /// </code>
    /// 89 bytes total. The verbatim 81-byte literal is the
    /// <c>g_GlobalErrorMsg[12]</c> entry at
    /// proxy/ClientToServer_linux_stubs.cpp:241 — note the DOUBLE space
    /// after "logins." before "Please" (preserved verbatim from the
    /// retail wire). The "+7" offset is a quirk of the kyp wire
    /// protocol preserved verbatim by the Linux port; the retail
    /// client subtracts it back out when displaying the error.
    /// </para>
    ///
    /// <para>
    /// Concrete regressions THIS sibling catches that the existing
    /// substring test does NOT:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>g_GlobalErrorMsg[12] literal drift.</b> Any change to the
    ///     verbatim string (typo fix, modern punctuation, the double-
    ///     space → single-space "cleanup", "logins" → "log-ins") at
    ///     ClientToServer_linux_stubs.cpp:241 would change the byte
    ///     content AND likely msg_len. Substring test still passes on
    ///     "not currently accepting new logins" any of those edits leave
    ///     intact; byte-exact pin trips immediately.
    ///   </item>
    ///   <item>
    ///     <b>msg_len byte-order regression.</b> The current code writes
    ///     msg_len via <c>*((int *) p) = (int) msg_len</c> — host-byte-
    ///     order, LE on Linux. A "fix" to <c>htonl()</c> would flip it
    ///     to BE: bytes 0x51 0x00 0x00 0x00 → 0x00 0x00 0x00 0x51. The
    ///     existing test's <c>BinaryPrimitives.ReadUInt32LittleEndian</c>
    ///     would then read msgLen=0x51000000=1359020032, far larger
    ///     than span.Length so the <c>span.Length &gt;= 8 + msgLen</c>
    ///     assertion would actually catch it — but Wave 114 catches the
    ///     more subtle case where someone "fixes" both writes to be the
    ///     same endianness symmetrically (both LE or both BE), making
    ///     the test still parse but the wire differ from retail.
    ///   </item>
    ///   <item>
    ///     <b>err+7 byte-order regression.</b> The current code writes
    ///     <c>ntohl((uint32_t)(Error + 7))</c> producing BE bytes. A
    ///     "consistency" fix to drop the ntohl would emit LE, changing
    ///     bytes [4..8) from 0x00 0x00 0x00 0x13 to 0x13 0x00 0x00 0x00.
    ///     The existing test reads as BE then subtracts 7 — flipped to
    ///     LE that becomes 0x13000000 - 7, errCode mismatch is caught
    ///     loudly; but Wave 114's exact byte pin documents the BE
    ///     contract explicitly.
    ///   </item>
    ///   <item>
    ///     <b>err+7 vs err+other-offset regression.</b> A revert of the
    ///     "+7" preserved-quirk to "+0" or any other value would change
    ///     bytes [4..8) but leave the msg unchanged. The existing test
    ///     subtracts 7 explicitly so it catches +0 / +6 / +8 already —
    ///     Wave 114 cross-checks via exact byte values.
    ///   </item>
    ///   <item>
    ///     <b>SendResponse trailing-bytes leak.</b> If
    ///     <c>SendResponse</c> at the bottom of GlobalError were
    ///     mis-sized (e.g. passes <c>sizeof(buffer)</c>=1024 instead of
    ///     the <c>p - buffer</c> computed length), the wire would carry
    ///     935 trailing bytes of buffer garbage. Existing test's
    ///     <c>span.Length &gt;= 8 + msgLen</c> still passes; Wave 114's
    ///     <c>Assert.Equal(89, span.Length)</c> catches.
    ///   </item>
    ///   <item>
    ///     <b>Server-side ProcessTicketInfo branch mis-selection.</b>
    ///     If the server-side dispatcher at UDP_Global.cpp emitted a
    ///     different G_ERROR code (e.g. 11 G_ERROR_NET7_INTERNAL or 9
    ///     G_ERROR_INACTIVE_ACCOUNT) for status=0 accounts, the proxy
    ///     would look up a DIFFERENT g_GlobalErrorMsg[] entry and emit
    ///     a different verbatim literal. Wave 114's verbatim-msg pin
    ///     catches any such re-routing; the existing test's substring
    ///     would only fire if the chosen alternative also happened to
    ///     omit "not currently accepting new logins".
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity POSITIVE. The stress-test rejection is exactly
    /// what the retail server's status-100 gate produced for status=0
    /// accounts in the retail "stress test closed" window; the proxy's
    /// verbatim g_GlobalErrorMsg[12] literal is preserved from the
    /// retail wire (double-space after "logins." included). No client
    /// stimulus addition, no server change, no widened input acceptance.
    /// </para>
    ///
    /// <para>
    /// Budget: 30s. AuthLogin sub-second; GlobalConnect → GlobalError
    /// round-trip sub-second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task StressTestClosedAccount_GlobalConnect_PinsExactReplyWireShape()
    {
        var account = TestAccounts.StressTestClosed;

        const string ExpectedLiteral =
            "Sorry, the server is not currently accepting new logins.  Please try again later.";
        const int ExpectedLiteralByteCount = 81;
        const int ExpectedReplyPayloadLength = 89;
        const int ExpectedRawErrCodeBE = 19;  // err+7 where err=12

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password),
            cts.Token);
        Assert.True(login.Valid,
            $"login should succeed (LinuxAuth ignores status): {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.GlobalHost, _server.GlobalPort, cts.Token);

        byte[] ticketBytes = Encoding.ASCII.GetBytes(login.Ticket!);
        byte[] payload = new byte[4 + ticketBytes.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), (uint)ticketBytes.Length);
        ticketBytes.CopyTo(payload, 4);
        payload[^1] = 0;

        var packet = Packet.ForOpcode(
            OpcodeId.Known.GlobalConnect.Value,
            payload);

        await conn.SendAsync(packet, cts.Token);

        Packet? errReply = null;
        while (true)
        {
            var p = await conn.ReceiveAsync(cts.Token);
            Assert.NotNull(p);

            if (p!.Header.Opcode == 0x0075)
            {
                errReply = p;
                break;
            }

            if (p.Header.Opcode == OpcodeId.Known.GlobalAvatarList.Value)
            {
                throw new Xunit.Sdk.XunitException(
                    "server returned GlobalAvatarList for a STRESS_TEST_CLOSED " +
                    "(status=0) account — server status check has been weakened.");
            }
        }

        Assert.NotNull(errReply);
        var span = errReply!.Payload.Span;

        Assert.Equal(ExpectedReplyPayloadLength, span.Length);

        // msg_len: u32 LE (host-byte-order on Linux x86_64).
        Assert.Equal((uint)ExpectedLiteralByteCount,
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0, 4)));

        // err+7: u32 BE (ntohl-produced big-endian bytes).
        Assert.Equal(ExpectedRawErrCodeBE,
            BinaryPrimitives.ReadInt32BigEndian(span.Slice(4, 4)));

        // Verbatim 81-byte literal (preserves the double-space after
        // "logins." from retail).
        Assert.Equal(ExpectedLiteral,
            Encoding.ASCII.GetString(span.Slice(8, ExpectedLiteralByteCount)));
    }
}
