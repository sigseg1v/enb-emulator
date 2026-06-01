// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0025 ITEM_BASE. Wire (variable, built by ItemBase::BuildItemBasePacket()):
///   int32 ItemTemplateID (BE, AddDataFlip4); uint8 Category; uint8 SubCategory;
///   uint8 ItemType; uint8 ItemFieldCount; variable item-field/effect data;
///   AddDataLS Name + Description + Manufacturer at tail (int16 len + char[len], no NUL).
/// Source: ItemBase.cpp BuildItemBasePacket(). Header decoded structurally;
/// trailing strings extracted by AddDataLS scan; middle bytes shown as unknown.
/// </summary>
public sealed class ItemBaseRecord : PacketRecord
{
    public ItemBaseRecord(ReadOnlySpan<byte> payload) : base(0x0025, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"ITEM_BASE truncated -- {'{'}Payload.Length{'}'} bytes, expected >= 8"); return; }
        int  templateId  = ReadI32BE(Payload, 0);
        byte category    = Payload[4];
        byte subCategory = Payload[5];
        byte itemType    = Payload[6];
        byte fieldCount  = Payload[7];
        FHex(sb, 0, "ItemTemplateID", templateId, "(BE)");
        FDec(sb, 4, "Category",    category,
            category == 10  ? "(Weapon)"           : category == 11  ? "(Device)"          :
            category == 12  ? "(CoreItem)"          : category == 13  ? "(Consumable)"      :
            category == 50  ? "(ElectronicItem)"    : category == 51  ? "(ReactorComponent)":
            category == 52  ? "(FabricatedItem)"    : category == 53  ? "(WeaponComponent)" :
            category == 54  ? "(AmmoComponent)"     : category == 100 ? "(Ammo)"            :
            category == 110 ? "(RewardItem)"        : null);
        FDec(sb, 5, "SubCategory",  subCategory);
        FDec(sb, 6, "ItemType",     itemType);
        FDec(sb, 7, "FieldCount",   fieldCount);
        // Extract trailing AddDataLS strings (int16 len + char[len], no NUL).
        var strings = ExtractAddDataLSStrings(Payload.AsSpan(8));
        if (strings.Count >= 1) { var (soff, sv) = strings[^Math.Min(3, strings.Count)]; FStr(sb, soff + 8, 2 + sv.Length, "Name",         sv, required: true); }
        if (strings.Count >= 2) { var (soff, sv) = strings[^Math.Min(2, strings.Count)]; FStr(sb, soff + 8, 2 + sv.Length, "Description",  sv); }
        if (strings.Count >= 3) { var (soff, sv) = strings[^1];                          FStr(sb, soff + 8, 2 + sv.Length, "Manufacturer",  sv); }
    }

    private static List<(int offset, string value)> ExtractAddDataLSStrings(ReadOnlySpan<byte> span)
    {
        var result = new List<(int, string)>();
        int i = 0;
        while (i + 2 <= span.Length)
        {
            short len = (short)(span[i] | (span[i + 1] << 8));
            if (len > 0 && len < 512 && i + 2 + len <= span.Length)
            {
                bool allPrintable = true;
                for (int k = 0; k < len; k++) { byte b = span[i + 2 + k]; if (b < 0x20 || b >= 0x7F) { allPrintable = false; break; } }
                if (allPrintable) { result.Add((i, System.Text.Encoding.ASCII.GetString(span.Slice(i + 2, len)))); i += 2 + len; continue; }
            }
            i++;
        }
        return result;
    }
}
