// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00BE CONFIRMED_ACTION_OFFER. Wire (hardcoded buffer,
/// PlayerConnection.cpp:863 SendConfirmedActionOffer):
///   int32 ActionType (BIG-ENDIAN); int32 ActionId (BIG-ENDIAN);
///   int16 TextLen (LE); uint8 Text[TextLen].
/// The current server always emits ActionType=1, ActionId=0x65, Text="Message".
/// The two leading int32s are stored big-endian in the literal byte array
/// (`0x00,0x00,0x00,0x01` etc.), unlike the LE convention elsewhere.
/// </summary>
public sealed class ConfirmedActionOfferRecord : PacketRecord
{
    public ConfirmedActionOfferRecord(ReadOnlySpan<byte> payload) : base(0x00BE, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 10) { Flag(sb, $"CONFIRMED_ACTION_OFFER truncated -- {Payload.Length} bytes, expected >= 10"); return; }
        FHex(sb, 0, "ActionType", ReadI32BE(Payload, 0), "(BE)");
        FHex(sb, 4, "ActionId",   ReadI32BE(Payload, 4), "(BE)");
        int textLen = ReadU16LE(Payload, 8);
        FDec(sb, 8, "TextLen", (short)textLen);
        if (10 + textLen > Payload.Length)
        {
            Flag(sb, $"CONFIRMED_ACTION_OFFER: Text needs {textLen} bytes at offset 10, only {Payload.Length - 10} remain");
            return;
        }
        string text = Encoding.Latin1.GetString(Payload, 10, textLen);
        FStr(sb, 10, textLen, "Text", text);
    }
}
