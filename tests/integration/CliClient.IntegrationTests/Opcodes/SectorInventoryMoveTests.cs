// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 27 direct-reply round-trip: client sends a 24-byte 0x0027
/// INVENTORY_MOVE with an out-of-spec <c>FromInv</c>, expects the
/// server's "UNRECOGNISED INVENTORY MOVE!" reply back as a 0x001D
/// MESSAGE_STRING.
///
/// <para>
/// Wire layout (mirror of <c>common/include/net7/PacketStructures.h:243</c>):
/// <code>
///   [0..4)   int32 GameID
///   [4..8)   int32 FromInv
///   [8..12)  int32 FromSlot
///   [12..16) int32 ToInv
///   [16..20) int32 ToSlot
///   [20..24) int32 Num
/// </code>
/// 24 bytes total; <c>ATTRIB_PACKED</c> so no implicit padding.
/// </para>
///
/// <para>
/// Byte order. <c>Player::HandleInventoryMove</c>
/// (<c>server/src/PlayerConnection.cpp:2474</c>) ntohl-swaps every
/// field on entry — wire format is NETWORK byte order
/// (big-endian), unlike Wave 26's SKILL_ABILITY which is host-LE.
/// Each <c>BinaryPrimitives.WriteInt32BigEndian</c> below mirrors
/// the field-by-field <c>ntohl</c> calls at lines 2493-2498.
/// </para>
///
/// <para>
/// Server handler. After the m_Gating / IsIncapacitated early-returns
/// (<c>PlayerConnection.cpp:2477-2485</c>) the handler switches on
/// <c>InvMo.FromInv</c>. The switch covers cases 1 (Cargo), 2 (Equip),
/// 3 (Vault), 4 (Buy), 6 (Loot), 12 (Manu-Override), 14 (Manu-Result),
/// 16 (Trade), 18 (Loot-confirm). Anything else falls into the
/// <c>default:</c> arm at <c>PlayerConnection.cpp:3238</c>:
/// <code>
///     default:
///         SendVaMessage("UNRECOGNISED INVENTORY MOVE!\n"
///                       "Please submit a bug report\n");
///         break;
/// </code>
/// <c>SendVaMessage</c> (<c>PlayerClass.cpp:3415</c>) vsprintf_s's the
/// format string then calls <c>SendMessageString</c>
/// (<c>PlayerConnection.cpp:10974</c>) which builds a
/// <c>[short length][char color=0][string\0]</c> buffer and emits via
/// <c>SendOpcode(0x001D, ..., length + 3)</c> — per-client UDP queue,
/// no SendToSector login-stage race (same favourable structural
/// property Wave 26 exploited).
/// </para>
///
/// <para>
/// Why <c>FromInv=99</c>. The default arm exists explicitly so the
/// retail server can defensively reply to unrecognised FromInv values
/// rather than crash on the missing case. The "UNRECOGNISED INVENTORY
/// MOVE! / Please submit a bug report" string is the verbatim retail
/// error message. Sending FromInv=99 — well clear of every populated
/// case (max 18) — deterministically routes into the default arm
/// regardless of character state (no inventory, no manufacturing, no
/// trade-partner, no incapacitation). This is the same shape as Wave
/// 24 (ITEM_STATE Inventory!=2 UNRECOGNISED branch) and Wave 26
/// (SKILL_ABILITY out-of-range AbilityIndex) — exercising the server's
/// documented bad-input defense, not asking the server to do anything
/// new. Per CLAUDE.md server-integrity: the default arm IS the retail
/// behaviour for unknown FromInv.
/// </para>
///
/// <para>
/// Why not FromSlot/ToSlot=0 with a known FromInv. The case 1 (Cargo)
/// branch dereferences <c>ShipIndex()-&gt;Inventory.CargoInv.Item[FromSlot]</c>
/// without bounds checking — a regression in slot validation could
/// SEGV on slot 0 of an empty cargo. The cargo-to-cargo sub-branch
/// then calls <c>SendAuxShip()</c> which only emits when
/// <c>HasDiff()</c> is true; an empty-to-empty swap produces no diff
/// so no reply. The default arm is strictly cleaner: no array
/// dereference, no state mutation, no diff dependency, guaranteed
/// single-frame reply.
/// </para>
///
/// <para>
/// Direct-reply assertion vs. survival probe. The default arm
/// unconditionally emits a single 0x001D MESSAGE_STRING frame with a
/// deterministic substring — same direct-reply shape as Wave 24
/// (<see cref="SectorItemStateTests"/>) and Wave 26
/// (<see cref="SectorSkillAbilityTests"/>). Strictly stronger than a
/// survival probe.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>InvMove struct long-revert in PacketStructures.h:243.</b>
///     Currently 6× int32_t = 24B canonical. Widening any field to
///     <c>long</c> on Linux x86_64 (sizeof(long)==8) grows the struct
///     — FromInv would read from offset 8 (FromSlot's slot) instead of
///     offset 4. With our payload that's offset 8 = 0 (host LE of our
///     FromSlot=0) so ntohl(0)=0, which is NOT in the switch — would
///     still hit the default arm. False negative. But a partial widen
///     of just GameID would shift FromInv to offset 8 also reading 0;
///     widening FromInv itself would shift FromSlot/ToSlot reads.
///     Caught only indirectly via test failure if the bytes happened
///     to align onto a valid case. More reliably caught by the Wave 12
///     long sweep + grep guards.
///   </item>
///   <item>
///     <b>ntohl/htonl byte-order flip on FromInv.</b> The handler
///     ntohl-decodes every field. A regression that dropped the ntohl
///     would read FromInv as the BE bytes interpreted host-LE — our
///     99 (0x00000063 BE) would read as 0x63000000 = 1_660_944_384,
///     still not in the switch — would still hit default arm and
///     still pass. False negative on this exact payload. But sending
///     a value like FromInv=1 BE (0x00000001 → host-LE 0x01000000 =
///     16_777_216 — not in switch, default arm) would pass too —
///     while the test author intended case 1. Documented limitation;
///     the byte-order guard rides on payloads where the host-LE
///     interpretation lands on a different switch case.
///   </item>
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:455.</b>
///     Case label sits immediately before 0x0029 ITEM_STATE in the
///     hand-maintained ~200-entry switch. A copy-paste swap with
///     HandleItemState (Wave 24's 11B ItemState struct) would
///     mis-interpret the 24B InvMove as a tiny 11B ItemState — reading
///     Enable/Inventory/ItemNum from inside our GameID/FromInv bytes,
///     likely hitting the Inventory==2 mutation branch with garbage
///     ItemNum and dereferencing into m_Equip[…].
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x0027 is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>, so it hits the
///     <c>default:</c> arm and falls through to the bottom-of-switch
///     ForwardClientOpcode. A regression dropping this opcode would
///     surface as a timeout waiting for the MESSAGE_STRING reply.
///   </item>
///   <item>
///     <b>HandleInventoryMove default-arm removal.</b> A "tidy up the
///     switch" refactor that deletes the default arm would let
///     FromInv=99 silently fall through the switch with no reply
///     and break this test's drain-loop expectation — surfacing the
///     regression at test time rather than first time a real client
///     sends a bad opcode.
///   </item>
///   <item>
///     <b>SendVaMessage format-string regression.</b> The literal
///     contains newlines (<c>"!\nPlease submit a bug report\n"</c>);
///     a refactor that strips the newlines or changes the wording
///     would silently break a future parser. We only pin on the
///     leading substring "UNRECOGNISED INVENTORY MOVE" so newline
///     tweaks alone don't sink the test, but a complete reword would.
///   </item>
///   <item>
///     <b>0x001D MESSAGE_STRING SendOpcode width regression.</b> Same
///     SendOpcode→m_UDPQueue→SendPacketCache→0x2016
///     PACKET_SEQUENCE→proxy SendClientPacketSequence→TCP fan-out
///     path as Waves 8/24. The Phase K sizeof(int32_t) opcode-header
///     fix at PlayerConnection.cpp:127 keeps the per-client UDP queue
///     header at the canonical 4-byte width — a revert would shift
///     the reply opcode bytes and break TCP framing.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x0027 INVENTORY_MOVE is
/// what the retail Win32 client emits when the user drags an item
/// between inventory bins. The retail server's HandleInventoryMove
/// has the same default arm with the same verbatim error string —
/// preservation-grade fidelity, not a fabrication. The default arm
/// is the documented bad-input handler; we are not making the server
/// accept any new input shape, not loosening any security posture
/// (the default arm is strictly defensive), not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; INVENTORY_MOVE + reply round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorInventoryMoveTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorInventoryMoveTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task InventoryMove_UnrecognisedFromInv_ReceivesUnrecognisedErrorString()
    {
        // cli_test24 — Pool[22] (Pool skips index for cli_test10 which
        // is the out-of-pool STRESS_TEST_CLOSED fixture). Dedicated to
        // this test so its Create/Delete cycle doesn't collide with
        // Pool[3..21] which are owned by prior Phase K waves.
        var account = TestAccounts.Pool[22];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Mover", shipName: "MoverShip", cts.Token);

        try
        {
            // 0x0027 INVENTORY_MOVE — 24B canonical payload with
            // FromInv=99 (well outside the switch's 1/2/3/4/6/12/14/
            // 16/18 cases → default arm). Wire is BIG-ENDIAN (the
            // handler ntohl-decodes every field at PlayerConnection
            // .cpp:2493-2498).
            //   [0..4)   int32 GameID   = 0
            //   [4..8)   int32 FromInv  = 99
            //   [8..12)  int32 FromSlot = 0
            //   [12..16) int32 ToInv    = 0
            //   [16..20) int32 ToSlot   = 0
            //   [20..24) int32 Num      = 0
            byte[] payload = new byte[24];
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), 99);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(8, 4), 0);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(12, 4), 0);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(16, 4), 0);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(20, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.InventoryMove.Value, payload),
                cts.Token);

            // Drain inbound until we see a 0x001D MESSAGE_STRING whose
            // body contains "UNRECOGNISED INVENTORY MOVE". Post-handshake
            // the server may interleave other in-sector fan-out (NPC
            // chatter, state updates, etc.); a frame cap keeps a stalled
            // pipeline from masquerading as the outer-CTS timeout.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.MessageString.Value)
                    continue;

                // 0x001D wire layout (mirror of Player::SendMessageString
                // at server/src/PlayerConnection.cpp:10974):
                //   [0..2) short length    (LE; strlen(msg)+1)
                //   [2]    char  color
                //   [3..]  msg + '\0'
                var span = reply.Payload.Span;
                if (span.Length < 4) continue;

                // Skip the 3-byte header and read the NUL-terminated string.
                int nulIdx = span[3..].IndexOf((byte)0);
                if (nulIdx < 0) continue;

                string body = Encoding.ASCII.GetString(span.Slice(3, nulIdx));

                // Filter — other MESSAGE_STRING frames (e.g. login
                // chatter) may arrive first. Keep draining until we see
                // the one keyed by our INVENTORY_MOVE.
                if (!body.Contains("UNRECOGNISED INVENTORY MOVE", StringComparison.Ordinal))
                    continue;

                // Pin on the distinctive substring rather than the
                // whole string so punctuation/newline tweaks don't sink
                // the test. Full literal at PlayerConnection.cpp:3242:
                // "UNRECOGNISED INVENTORY MOVE!\nPlease submit a bug report\n"
                Assert.Contains("UNRECOGNISED INVENTORY MOVE", body);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0027 INVENTORY_MOVE (FromInv=99) " +
                $"without seeing 0x001D MESSAGE_STRING containing \"UNRECOGNISED INVENTORY MOVE\". " +
                $"Likely the server's HandleInventoryMove default-arm SendVaMessage path broke, " +
                $"the proxy default-case forwarding dropped the opcode, " +
                $"the dispatcher case at PlayerConnection.cpp:455 got mis-routed, " +
                $"or InvMove struct was reshuffled and FromInv read from the wrong offset.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
