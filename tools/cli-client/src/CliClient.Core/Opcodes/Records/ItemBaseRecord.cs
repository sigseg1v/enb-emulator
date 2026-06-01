// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0025 ITEM_BASE. Wire shape (variable, built by ItemBase::BuildItemBasePacket()):
///   int32  ItemTemplateID  (BE, AddDataFlip4)
///   uint8  Category
///   uint8  SubCategory
///   uint8  ItemType
///   uint8  ItemFieldCount
///   Variable: ItemFieldCount field-id/value pairs (id=1B, value=1-4B per type)
///   int32  ActivatableEffectsCount (BE)
///   Variable: per-activatable: AddDataLS name/desc/tooltip, int filler, int descVarCount, floats, 2 flags
///   int32  EquipableEffectsCount (BE)
///   Variable: per-equippable: same layout
///   int16  GameBaseAsset  (BE)
///   int16  IconBaseAsset  (BE)
///   int16  TechLevel      (BE)
///   int32  Cost           (BE)
///   int32  MaxStack       (BE)
///   int32  UseEffect      (BE)
///   uint8  Flags
///   AddDataLS Name         (int16 len + char[len], no NUL)
///   AddDataLS Description  (int16 len + char[len], no NUL)
///   AddDataLS Manufacturer (int16 len + char[len], no NUL)
/// Source: ItemBase.cpp BuildItemBasePacket().
/// The header (first 8 bytes) is decoded structurally; the variable middle
/// is skipped; the trailing strings are extracted via ASCII scan.
/// </summary>
public sealed class ItemBaseRecord : PacketRecord
{
    public ItemBaseRecord(ReadOnlySpan<byte> payload) : base(0x0025, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8)
        {
            Flag(sb, $"ITEM_BASE truncated -- {Payload.Length} bytes, expected >= 8");
            return;
        }
        int  templateId    = ReadI32BE(Payload, 0);  // AddDataFlip4 -- wire is BE
        byte category      = Payload[4];
        byte subCategory   = Payload[5];
        byte itemType      = Payload[6];
        byte fieldCount    = Payload[7];

        FieldHex(sb, "ItemTemplateID", templateId, "(BE)");
        FieldDec(sb, "Category",       category,
            category == 10  ? "(Weapon)" :
            category == 11  ? "(Device)" :
            category == 12  ? "(CoreItem)" :
            category == 13  ? "(Consumable)" :
            category == 50  ? "(ElectronicItem)" :
            category == 51  ? "(ReactorComponent)" :
            category == 52  ? "(FabricatedItem)" :
            category == 53  ? "(WeaponComponent)" :
            category == 54  ? "(AmmoComponent)" :
            category == 100 ? "(Ammo)" :
            category == 110 ? "(RewardItem)" : null);
        FieldDec(sb, "SubCategory",    subCategory);
        FieldDec(sb, "ItemType",       itemType);
        FieldDec(sb, "FieldCount",     fieldCount);

        // The trailing strings (Name, Description, Manufacturer) are AddDataLS-encoded
        // (int16 len + char[len], no NUL). Extract them from the end of payload.
        // Walk backwards: find the last three AddDataLS strings.
        var strings = ExtractAddDataLSStrings(Payload.AsSpan(8));
        if (strings.Count >= 1) FieldString(sb, "Name",         strings[^Math.Min(3, strings.Count)].value, requiredNonEmpty: true);
        if (strings.Count >= 2) FieldString(sb, "Description",  strings[^Math.Min(2, strings.Count)].value);
        if (strings.Count >= 3) FieldString(sb, "Manufacturer", strings[^1].value);
    }

    // Scan forward for AddDataLS-encoded strings (int16 length + chars).
    // Returns all (offset, value) pairs found in the span.
    private static List<(int offset, string value)> ExtractAddDataLSStrings(ReadOnlySpan<byte> span)
    {
        var result = new List<(int, string)>();
        int i = 0;
        while (i + 2 <= span.Length)
        {
            short len = (short)((span[i] | (span[i + 1] << 8)));
            if (len > 0 && len < 512 && i + 2 + len <= span.Length)
            {
                bool allPrintable = true;
                for (int k = 0; k < len; k++)
                {
                    byte b = span[i + 2 + k];
                    if (b < 0x20 || b >= 0x7F) { allPrintable = false; break; }
                }
                if (allPrintable)
                {
                    result.Add((i, System.Text.Encoding.ASCII.GetString(span.Slice(i + 2, len))));
                    i += 2 + len;
                    continue;
                }
            }
            i++;
        }
        return result;
    }
}
