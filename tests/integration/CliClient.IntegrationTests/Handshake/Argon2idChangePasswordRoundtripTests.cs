// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Auth;
using N7.CliClient.IntegrationTests.Opcodes;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Handshake;

/// <summary>
/// End-to-end regression test for Phase X (Argon2id password storage):
/// a user-tier <c>/changepassword &lt;newpw&gt;</c> slash command must
/// update the stored PHC so that a subsequent AuthLogin with the new
/// password succeeds and AuthLogin with the original password fails.
///
/// <para>
/// Exercises every link in the post-Phase-X password chain:
/// </para>
/// <list type="bullet">
///   <item><c>Player::HandleClientChat</c> -> <c>HandleSlashCommands</c>
///         -> <c>MatchOptWithParam("changepassword", ...)</c> arm at
///         PlayerConnection.cpp:5698. NO admin gate on this arm; any
///         logged-in user can change their own password.</item>
///   <item><c>AccountManager::ChangePassword</c>
///         (server/src/AccountManager.cpp:278): hashes via
///         <c>HashPasswordToPhc</c> (libsodium INTERACTIVE Argon2id,
///         m=64MiB, t=2, p=1) and runs
///         <c>UPDATE accounts SET password_phc = ? WHERE username = ?</c>
///         via the parameterised <c>sql_query_c</c> path. Plaintext
///         never crosses the SQL wire.</item>
///   <item><c>LinuxAuth.cpp::ValidateAccountLinux</c>: on the next
///         AuthLogin, reads <c>password_phc</c> from net7_user.accounts
///         and calls <c>crypto_pwhash_str_verify</c>. login-server is a
///         separate process from the sector server but they share
///         Postgres, so the UPDATE is visible immediately.</item>
/// </list>
///
/// <para>
/// Wave 189 already pins the missing-arg fork. This is the matching
/// success-path pin -- without it, a regression that silently broke
/// the Hash -> UPDATE -> verify chain (a libsodium version drift, a
/// column rename, a Postgres connection-mode change) would only be
/// caught by an operator noticing they could not log back in. Does
/// not advance the Phase K opcode-coverage ratchet: 0x0033
/// CLIENT_CHAT and 0x001D MESSAGE_STRING are both already covered.
/// </para>
///
/// <para>Budget: 120s. The initial handshake takes ~2s, the slash
/// reply ~0.5s, and each subsequent AuthLogin spends ~70ms inside
/// <c>crypto_pwhash_str_verify</c> (the whole point of the Argon2id
/// switch).</para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class Argon2idChangePasswordRoundtripTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public Argon2idChangePasswordRoundtripTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task SlashChangepassword_RewritesPhc_OldPasswordRejected_NewPasswordAccepted()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;
        const string newPassword = "phx_argon2_roundtrip_2026";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        // 1. First login with the original PHC (precomputed by TestAccounts).
        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"initial login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // 2. Drive the sector handshake and fire the slash command.
        await using (var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Cypria", shipName: "CypriaShip", cts.Token))
        {
            try
            {
                var codec = new ClientChatCodec();
                var chat = new ClientChatMessage(
                    GameId: session.GameId,
                    Type: ChatChannel.Group,
                    Message: $"/changepassword {newPassword}");

                await session.Sector.SendAsync(
                    Packet.ForOpcode(
                        OpcodeId.Known.ClientChat.Value,
                        codec.EncodeOutbound(chat)),
                    cts.Token);

                // Reply body: "Your password has been changed to: `<newpw>`"
                string expectedLiteral =
                    $"Your password has been changed to: `{newPassword}`";
                int expectedLiteralBytes =
                    Encoding.ASCII.GetByteCount(expectedLiteral);
                int expectedReplyPayloadLength =
                    2 + 1 + expectedLiteralBytes + 1; // u16 length + u8 color + literal + NUL
                short expectedReplyLengthField = (short)(expectedLiteralBytes + 1);
                const byte expectedReplyColor = 5;

                int framesSeen = 0;
                const int maxFrames = 400;
                bool seenReply = false;
                while (framesSeen++ < maxFrames)
                {
                    var reply = await session.Sector.ReceiveAsync(cts.Token);
                    Assert.NotNull(reply);

                    if (reply!.Header.Opcode != OpcodeId.Known.MessageString.Value)
                        continue;

                    var span = reply.Payload.Span;
                    if (span.Length < 4) continue;

                    short msgLen = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                    if (msgLen < 1) continue;

                    int bodyBytes = Math.Min(msgLen - 1, span.Length - 3);
                    if (bodyBytes <= 0) continue;

                    string text = Encoding.ASCII.GetString(span.Slice(3, bodyBytes));
                    if (!text.Equals(expectedLiteral, StringComparison.Ordinal))
                        continue;

                    Assert.Equal(expectedReplyPayloadLength, span.Length);
                    Assert.Equal(expectedReplyLengthField, msgLen);
                    Assert.Equal(expectedReplyColor, span[2]);
                    Assert.Equal((byte)0x00, span[3 + expectedLiteralBytes]);
                    seenReply = true;
                    break;
                }

                if (!seenReply)
                {
                    throw new Xunit.Sdk.XunitException(
                        $"drained {maxFrames} frames after 0x0033 \"/changepassword {newPassword}\" without seeing the success 0x001D reply.");
                }
            }
            finally
            {
                using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
                catch { /* best-effort cleanup */ }
            }
        }

        // 3. AuthLogin with the ORIGINAL password must now fail.
        using (var oldCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            var oldRetry = await _client.AuthLogin.LoginAsync(
                new AuthLoginRequest(account.Username, account.Password), oldCts.Token);
            Assert.False(oldRetry.Valid,
                $"original password should be rejected after /changepassword; got Valid=true. raw: {oldRetry.RawBody.TrimEnd()}");
            Assert.True(string.IsNullOrEmpty(oldRetry.Ticket));
        }

        // 4. AuthLogin with the NEW password must succeed.
        using (var newCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            var newLogin = await _client.AuthLogin.LoginAsync(
                new AuthLoginRequest(account.Username, newPassword), newCts.Token);
            Assert.True(newLogin.Valid,
                $"new password should be accepted after /changepassword; raw: {newLogin.RawBody.TrimEnd()}");
            Assert.False(string.IsNullOrEmpty(newLogin.Ticket));
        }
    }
}
