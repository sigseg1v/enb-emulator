// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins <see cref="AuxDataRecord.TryExtractSummary"/> -- the world-model's
/// extractor that pulls an object's own Name, CombatLevel, and ship MaxSpeed out
/// of a 0x001B AUX frame. The schema is the ShipIndex flavour
/// (AuxSchemas.ShipIndex / server AuxShipIndex.cpp): flag 0 = Name(Str),
/// flag 13 = MaxSpeed(F32), flag 26 = CombatLevel(U32). A field with flagNum N is
/// present iff bit (N+4) is set, and present fields serialise in flag order
/// (AuxBase.cpp CheckAuxBit -- see AuxWalker).
/// </summary>
public sealed class AuxDataRecordSummaryTests
{
    /// <summary>
    /// Build a minimal ShipIndex 0x001B payload carrying only the requested
    /// fields, in flag order: [u32 GameID][u16 BodyLen=len-6][u8 ver=1]
    /// [8 flag bytes][Name][MaxSpeed][CombatLevel].
    /// </summary>
    private static byte[] ShipAux(uint gameId, string? name, uint? combatLevel, float? maxSpeed)
    {
        var flags = new byte[8];
        void SetFlag(int flagNum) { int bit = flagNum + 4; flags[bit / 8] |= (byte)(1 << (bit % 8)); }

        var body = new List<byte>();
        void U16(int v) { body.Add((byte)(v & 0xFF)); body.Add((byte)((v >> 8) & 0xFF)); }
        void U32(uint v) { for (int k = 0; k < 4; k++) body.Add((byte)((v >> (8 * k)) & 0xFF)); }
        void F32(float v) { var b = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(b, v); body.AddRange(b); }

        // Flag order is ascending: Name(0), MaxSpeed(13), CombatLevel(26).
        if (name is not null)
        {
            SetFlag(0);
            var nb = Encoding.ASCII.GetBytes(name);
            U16(nb.Length);
            body.AddRange(nb);
        }
        if (maxSpeed is { } ms) { SetFlag(13); F32(ms); }
        if (combatLevel is { } lvl) { SetFlag(26); U32(lvl); }

        var p = new byte[7 + 8 + body.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), gameId);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), (ushort)(p.Length - 6)); // BodyLen
        p[6] = 1;                                                                          // version
        flags.CopyTo(p.AsSpan(7, 8));
        body.CopyTo(p, 15);
        return p;
    }

    [Fact]
    public void ShipIndex_ExtractsNameLevelAndMaxSpeed()
    {
        byte[] p = ShipAux(0x40000050, "Trader Bob", combatLevel: 42, maxSpeed: 1500f);

        var sum = AuxDataRecord.TryExtractSummary(p);

        Assert.NotNull(sum);
        Assert.Equal(0x40000050u, sum!.Value.GameId);
        Assert.Equal("Trader Bob", sum.Value.Name);
        Assert.Equal(42u, sum.Value.CombatLevel);
        Assert.Equal(1500f, sum.Value.MaxSpeed!.Value, 3);
    }

    [Theory]
    [InlineData(1500f)]
    [InlineData(1850.5f)]
    [InlineData(12.25f)]
    public void MaxSpeed_RoundTripsThroughTheFloatFormatter(float speed)
    {
        // The walker renders F32 with "0.0##"; these values survive that exactly.
        byte[] p = ShipAux(0x40000051, "X", combatLevel: null, maxSpeed: speed);

        var sum = AuxDataRecord.TryExtractSummary(p);

        Assert.NotNull(sum);
        Assert.Equal(speed, sum!.Value.MaxSpeed!.Value, 3);
        Assert.Null(sum.Value.CombatLevel);   // no combat-level flag was set
    }

    [Fact]
    public void NoMaxSpeedFlag_LeavesMaxSpeedNull_LevelStillRead()
    {
        // Regression guard: a ship that never announces MaxSpeed must not
        // synthesise one (older frames, and most non-self ships).
        byte[] p = ShipAux(0x40000052, "Mob Ship", combatLevel: 7, maxSpeed: null);

        var sum = AuxDataRecord.TryExtractSummary(p);

        Assert.NotNull(sum);
        Assert.Equal("Mob Ship", sum!.Value.Name);
        Assert.Equal(7u, sum.Value.CombatLevel);
        Assert.Null(sum.Value.MaxSpeed);
    }

    [Fact]
    public void NameOnly_LevelAndMaxSpeedNull()
    {
        byte[] p = ShipAux(0x40000053, "Lonely", combatLevel: null, maxSpeed: null);

        var sum = AuxDataRecord.TryExtractSummary(p);

        Assert.NotNull(sum);
        Assert.Equal("Lonely", sum!.Value.Name);
        Assert.Null(sum.Value.CombatLevel);
        Assert.Null(sum.Value.MaxSpeed);
    }

    [Fact]
    public void TruncatedPayload_ReturnsNull()
    {
        Assert.Null(AuxDataRecord.TryExtractSummary(new byte[7]));  // < 8 bytes
    }

    [Fact]
    public void WrongVersionByte_ReturnsNull()
    {
        byte[] p = ShipAux(0x40000054, "Trader Bob", combatLevel: 42, maxSpeed: 1500f);
        p[6] = 2;   // version must be 1
        Assert.Null(AuxDataRecord.TryExtractSummary(p));
    }

    [Fact]
    public void MaxSpeedValue_IsByteExactInThePayload()
    {
        // Pin the actual wire bytes for MaxSpeed=1500 so a layout drift is caught.
        byte[] p = ShipAux(0x40000055, "Z", combatLevel: null, maxSpeed: 1500f);
        // body starts at 15: Name = u16(1) + 'Z'  -> 3 bytes; MaxSpeed at 15+3 = 18.
        float onWire = BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(18, 4));
        Assert.Equal(1500f, onWire);
        Assert.Equal(new byte[] { 0x00, 0x80, 0xBB, 0x44 }, p[18..22]);  // 1500.0f LE
    }
}
