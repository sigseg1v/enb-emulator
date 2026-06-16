// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00C8 GUILD_RECRUIT_CONFIRM_SECTOR. Wire (PlayerGuild.cpp:831
/// SendGuildRecruitConfirmSector):
///   AddDataLS RecruiterName; AddDataLS GuildName
///   (each = uint16 len LE + len raw bytes, no NUL).
/// </summary>
public sealed class GuildRecruitConfirmRecord : PacketRecord
{
    public GuildRecruitConfirmRecord(ReadOnlySpan<byte> payload) : base(0x00C8, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        int off = 0;
        if (!TryReadAddDataLS(sb, ref off, "RecruiterName")) return;
        if (!TryReadAddDataLS(sb, ref off, "GuildName")) return;
    }
}
