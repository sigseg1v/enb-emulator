// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0005 START. Wire: int32 StartID (4 bytes, LE) -- the client's own in-sector
/// avatar object id; the client adopts it as its self-id and the server keys all
/// avatar-scoped packets in that sector to it. In capture_1's first sector the
/// SAME value 10069 leads the GalaxyMap (0x97 #351), the avatar skill/faction
/// list (0xA3 #550+), the StarbaseSet-list (0x4E #638) and this START (#387).
/// The value is sector-ASSIGNED (it changes on every sector entry: 10069, 8865,
/// 3126, 8873, ...) and carries NO PLAYER_TAG bits -- so it is neither the
/// global PLAYER_TAG'd GameID nor the constant CharacterID (8865 in a later
/// sector would decode to a different account under account*5+slot+1, which is
/// impossible for one character). Our server emits SendStart(player->CharacterID())
/// (PlayerConnection.cpp:1079, called from SectorManager.cpp:387/551); retail
/// uses a small sector-local id instead -- an id-allocation difference, not a
/// wire-format one (both are a bare LE int32).
/// </summary>
public sealed class StartRecord : PacketRecord
{
    public StartRecord(ReadOnlySpan<byte> payload) : base(0x0005, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"START truncated -- {Payload.Length} bytes, expected 4"); return; }
        int startId = ReadI32LE(Payload, 0);
        FHex(sb, 0, "StartID", startId, "(client's in-sector avatar id; sector-assigned)");
        FlagSuspicious(sb, "StartID", startId);
        if (Payload.Length > 4) Flag(sb, $"START has {Payload.Length - 4} trailing bytes");
    }
}
