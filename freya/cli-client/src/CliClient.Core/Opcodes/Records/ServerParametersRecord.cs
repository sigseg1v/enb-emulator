// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0042 SERVER_PARAMETERS. Wire (70 bytes, PACKED -- no alignment padding):
///   float ZBandMin; float ZBandMax; float XMin; float YMin; float XMax; float YMax;
///   float FogNear; float FogFar; int32 DebrisMode;
///   uint8 LightBackdrop; uint8 FogBackdrop; uint8 SwapBackdrop;
///   float BackdropFogNear; float BackdropFogFar; float MaxTilt;
///   uint8 AutoLevel; float ImpulseRate; float DecayVelocity; float DecaySpin;
///   int16 BackdropBaseAsset; uint32 SectorNum.
/// </summary>
public sealed class ServerParametersRecord : PacketRecord
{
    public ServerParametersRecord(ReadOnlySpan<byte> payload) : base(0x0042, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 70) { Flag(sb, $"SERVER_PARAMETERS truncated -- {Payload.Length} bytes, expected 70"); return; }

        FFloat(sb,  0, "ZBandMin",        ReadF32LE(Payload,  0));
        FFloat(sb,  4, "ZBandMax",        ReadF32LE(Payload,  4));
        FFloat(sb,  8, "XMin",            ReadF32LE(Payload,  8));
        FFloat(sb, 12, "YMin",            ReadF32LE(Payload, 12));
        FFloat(sb, 16, "XMax",            ReadF32LE(Payload, 16));
        FFloat(sb, 20, "YMax",            ReadF32LE(Payload, 20));
        FFloat(sb, 24, "FogNear",         ReadF32LE(Payload, 24));
        FFloat(sb, 28, "FogFar",          ReadF32LE(Payload, 28));
        FDec(sb,   32, "DebrisMode",      ReadI32LE(Payload, 32));
        FDec(sb,   36, "LightBackdrop",   Payload[36]);
        FDec(sb,   37, "FogBackdrop",     Payload[37]);
        FDec(sb,   38, "SwapBackdrop",    Payload[38]);
        FFloat(sb, 39, "BackdropFogNear", ReadF32LE(Payload, 39));
        FFloat(sb, 43, "BackdropFogFar",  ReadF32LE(Payload, 43));
        FFloat(sb, 47, "MaxTilt",         ReadF32LE(Payload, 47));
        FDec(sb,   51, "AutoLevel",       Payload[51]);
        FFloat(sb, 52, "ImpulseRate",     ReadF32LE(Payload, 52));
        FFloat(sb, 56, "DecayVelocity",   ReadF32LE(Payload, 56));
        FFloat(sb, 60, "DecaySpin",       ReadF32LE(Payload, 60));
        FDec(sb,   64, "BackdropBaseAsset", ReadI16LE(Payload, 64));
        FHex(sb,   66, "SectorNum",       ReadU32LE(Payload, 66));
    }
}
