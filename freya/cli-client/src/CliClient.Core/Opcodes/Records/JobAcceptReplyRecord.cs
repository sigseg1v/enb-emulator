// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0096 JOB_ACCEPT_REPLY. Two server-emitted shapes
/// (PlayerConnection.cpp:10016 / :10027):
///   - 4 bytes: int32 JobID (host-order LE; = pkt-&gt;StarbaseID echoed back) for
///     a specific accepted job.
///   - 0 bytes: empty reply (the generic "accept job 9" branch).
/// </summary>
public sealed class JobAcceptReplyRecord : PacketRecord
{
    public JobAcceptReplyRecord(ReadOnlySpan<byte> payload) : base(0x0096, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length == 0) { Flag(sb, "JOB_ACCEPT_REPLY empty (generic accept -- no JobID body)"); return; }
        if (Payload.Length < 4) { Flag(sb, $"JOB_ACCEPT_REPLY truncated -- {Payload.Length} bytes, expected 0 or 4"); return; }
        FHex(sb, 0, "JobID", ReadI32LE(Payload, 0));
    }
}
