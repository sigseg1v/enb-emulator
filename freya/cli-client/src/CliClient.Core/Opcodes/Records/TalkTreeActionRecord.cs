// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0056 TALK_TREE_ACTION. Server tells the client which talk-tree action /
/// branch to execute. 4 bytes:
///   int32  Action  @0
/// </summary>
public sealed class TalkTreeActionRecord : PacketRecord
{
    public TalkTreeActionRecord(ReadOnlySpan<byte> payload) : base(0x0056, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"TALK_TREE_ACTION truncated -- {Payload.Length} bytes, expected 4"); return; }
        FDec(sb, 0, "Action", ReadI32LE(Payload, 0));
    }
}
