// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0093 JOB_LIST. Wire (PlayerConnection.cpp:9950 /
/// SectorManager::GetJobList:1521):
///   int32 CountPlaceholder (LE) -- NOTE: emitter writes m_JobListCount, but
///   only the AVAILABLE jobs are appended, so this field is an UPPER BOUND, not
///   the true entry count. Parse entries until the payload is exhausted.
///   Each entry: int32 ID; int32 Category; int32 Unknown(0); int32 Level;
///   AddDataSN Title; AddDataSN Sponsor; AddDataSN Reward (each NUL-terminated).
/// </summary>
public sealed class JobListRecord : PacketRecord
{
    public JobListRecord(ReadOnlySpan<byte> payload) : base(0x0093, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"JOB_LIST truncated -- {Payload.Length} bytes, expected >= 4"); return; }
        FDec(sb, 0, "CountPlaceholder", ReadI32LE(Payload, 0), "(upper bound, not entry count)");
        int off = 4;
        int i = 0;
        while (off < Payload.Length)
        {
            if (off + 16 > Payload.Length)
            {
                Flag(sb, $"JOB_LIST: entry [{i}] needs 16 bytes of header at offset {off}, only {Payload.Length - off} remain");
                return;
            }
            FHex(sb, off,      $"[{i}].ID",       ReadI32LE(Payload, off));
            FDec(sb, off + 4,  $"[{i}].Category", ReadI32LE(Payload, off + 4));
            FDec(sb, off + 8,  $"[{i}].Unknown",  ReadI32LE(Payload, off + 8));
            FDec(sb, off + 12, $"[{i}].Level",    ReadI32LE(Payload, off + 12));
            off += 16;
            if (!TryReadCString(sb, ref off, $"[{i}].Title"))   return;
            if (!TryReadCString(sb, ref off, $"[{i}].Sponsor")) return;
            if (!TryReadCString(sb, ref off, $"[{i}].Reward"))  return;
            i++;
        }
    }
}
