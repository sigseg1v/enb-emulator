// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x2020 LOGIN_STAGE_S_C. Server-to-client login progress marker. 4 bytes:
///   int32  Stage  @0   (the client advances its loading-screen state machine)
/// </summary>
public sealed class LoginStageRecord : PacketRecord
{
    public LoginStageRecord(ReadOnlySpan<byte> payload) : base(0x2020, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"LOGIN_STAGE_S_C truncated -- {Payload.Length} bytes, expected 4"); return; }
        FDec(sb, 0, "Stage", ReadI32LE(Payload, 0));
    }
}
