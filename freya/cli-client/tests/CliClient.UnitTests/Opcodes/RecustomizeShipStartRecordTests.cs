// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins 0x0081 RECUSTOMIZE_SHIP_START -- the last server-emitted 0x00xx
/// opcode that previously fell through to <see cref="GenericRecord"/>. Wire is
/// the verbatim 262-byte RecustomizeShipStart struct: a 194-byte ShipData (5
/// int32 ids, 26-byte NUL name, float[3] name colour, 8x 17-byte ColorInfo) +
/// int32 costs[12] (LE) + int32 PlayerID (BE, htonl) + int32 unknown[4].
/// </summary>
public sealed class RecustomizeShipStartRecordTests
{
    public RecustomizeShipStartRecordTests() => AnsiPalette.Enabled = false;

    private static string Dump(ushort opcode, byte[] payload)
        => PacketRecord.Resolve(opcode, payload).DumpToString();

    [Fact]
    public void RecustomizeShipStart_FullParse_AllBytesConsumed()
    {
        var b = new byte[262];
        void I32LE(int off, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(off), v);
        void I32BE(int off, int v) => BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(off), v);
        void F32(int off, float v) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(off), v);

        // ShipData ids
        I32LE(0x00, 1);          // race
        I32LE(0x04, 3);          // profession
        I32LE(0x08, 0x100);      // hull
        I32LE(0x0C, 0x200);      // wing
        I32LE(0x10, 0x300);      // decal
        // ship name (NUL-terminated within 26 bytes)
        Encoding.Latin1.GetBytes("Wanderer").CopyTo(b, 0x14);
        // name colour
        F32(0x2E, 1.0f); F32(0x32, 0.5f); F32(0x36, 0.25f);
        // 8 ColorInfo blocks starting at 0x3A, 17 bytes each
        for (int i = 0; i < 8; i++)
        {
            int off = 0x3A + i * 17;
            F32(off, 0.1f * i); F32(off + 4, 0.2f); F32(off + 8, 0.3f);
            b[off + 12] = (byte)(i & 1);            // flat
            I32LE(off + 13, 0x10 + i);              // metal
        }
        // costs[12] @ 0xC2
        for (int i = 0; i < 12; i++) I32LE(194 + i * 4, 100 + i);
        // PlayerID @ 0xF2 (BE)
        I32BE(0xF2, 0x000ABCDE);
        // unknown[4] @ 0xF6 left zero

        var dump = Dump(0x0081, b);
        Assert.Contains("ship.Race", dump);
        Assert.Contains("ship.Hull", dump);
        Assert.Contains("ship.Name", dump);
        Assert.Contains("Wanderer", dump);
        Assert.Contains("ship.NameColor.H", dump);
        Assert.Contains("ship.HullPrimary.Metal", dump);
        Assert.Contains("ship.EngineSecondary.V", dump);
        Assert.Contains("Cost[0]", dump);
        Assert.Contains("Cost[11]", dump);
        Assert.Contains("PlayerID", dump);
        Assert.Contains("0x000ABCDE", dump);
        Assert.Contains("Unknown[3]", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void RecustomizeShipStart_Short_Flags()
    {
        var dump = Dump(0x0081, new byte[100]);
        Assert.Contains("truncated", dump);
    }
}
