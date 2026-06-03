// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// STARBASE_REQUEST (0x004E, client->server) -- what the client sends while docked
/// at a station: leave the station, talk to an NPC, work the job terminal, accept a
/// job, or open the avatar/ship customiser. Wire (struct StarbaseRequest, 9 bytes):
/// int32 PlayerID, int32 StarbaseID, char Action. ALL LITTLE-ENDIAN. The consumer,
/// Player::HandleStarbaseRequest (PlayerConnection.cpp:9854), reads pkt-&gt;PlayerID,
/// pkt-&gt;StarbaseID and switch(pkt-&gt;Action) directly -- there is NO ntohl anywhere
/// in the handler -- so the two int32s are host-order little-endian and Action is a
/// single byte. Action is switched on: 1 leave station, 4 talk to NPC, 6 activate
/// job terminal, 7 job description, 8/9 accept job, 10 customise avatar, 11 customise
/// starship. The capture corroborates the byte order: across the session's eight
/// 0x4E frames every PlayerID and StarbaseID is a small sane id little-endian
/// (5150 / 10001 / 10012 / 15077, 2939 / 45151 / ...) and every Action is a valid
/// member {1, 4}; big-endian would make all the ids absurd (0x1E140000+). PlayerID
/// 5150 in session .44:3029 is the SAME avatar the same session's 0x05 START / 0x06
/// START_ACK carry as their (little-endian) StartID -- a cross-packet lock on the
/// byte order. StarbaseID is a context id whose meaning depends on Action (the NPC
/// target for Action 4, the job id for 7, etc.). Source: struct StarbaseRequest
/// (PacketStructures.h:812), Player::HandleStarbaseRequest (PlayerConnection.cpp:9854).
/// Pinned to capture_3.rar.
/// </summary>
public sealed class StarbaseRequestRecord : PacketRecord
{
    public StarbaseRequestRecord(ReadOnlySpan<byte> payload) : base(0x004E, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 9) { Flag(sb, $"STARBASE_REQUEST truncated -- {Payload.Length} bytes, expected 9"); return; }

        FHex(sb, 0, "PlayerID", ReadI32LE(Payload, 0),
            "(LE; player avatar id -- read directly, no ntohl; matches the session's 0x05/0x06 StartID)");
        FHex(sb, 4, "StarbaseID", ReadI32LE(Payload, 4),
            "(LE; context id -- NPC target/job id/etc., meaning depends on Action)");

        byte action = Payload[8];
        string name = action switch
        {
            1  => "leave station",
            4  => "talk to NPC",
            6  => "activate job terminal",
            7  => "job description",
            8  => "accept job",
            9  => "accept job",
            10 => "customise avatar",
            11 => "customise starship",
            _  => "unknown action -- handler default",
        };
        FDec(sb, 8, "Action", action, $"({name})");

        if (Payload.Length > 9) Flag(sb, $"STARBASE_REQUEST has {Payload.Length - 9} trailing bytes");
    }
}
