// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0094 JOB_DESCRIPTION. Wire (PlayerConnection.cpp:9971 /
/// SectorManager::GetJobDescription:1558):
///   int32 JobID (LE); uint8 Available (0/1);
///   AddDataSN Title (NUL-terminated); Description (NUL-terminated).
/// AddDataSN = raw bytes + NUL, no length prefix.
/// </summary>
public sealed class JobDescriptionRecord : PacketRecord
{
    public JobDescriptionRecord(ReadOnlySpan<byte> payload) : base(0x0094, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 5) { Flag(sb, $"JOB_DESCRIPTION truncated -- {Payload.Length} bytes, expected >= 5"); return; }
        FHex(sb, 0, "JobID", ReadI32LE(Payload, 0));
        FDec(sb, 4, "Available", Payload[4], Payload[4] != 0 ? "(yes)" : "(no)");
        int off = 5;
        if (!TryReadCString(sb, ref off, "Title")) return;
        if (!TryReadCString(sb, ref off, "Description")) return;
    }
}
