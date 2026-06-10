// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Phase AB §3 (spec half). Pins the 0x2012 START_PROSPECT -&gt; 0x000B
/// OBJECT_TO_OBJECT_EFFECT fabrication contract the proxy implements in
/// <c>UDPClient::StartProspecting</c> (proxy/UDPProxyToClient_linux.cpp).
///
/// IMPORTANT -- what this is and is NOT:
///   * It is a SPEC mirror: it re-states, in C#, the exact bytes the proxy
///     fabricates, then decodes them with the real production
///     <see cref="ObjectToObjectEffectRecord"/> and asserts the field values.
///     It is the byte reference the future LIVE harness will assert equality
///     against.
///   * It is NOT a live round-trip. The real round-trip (drive the live
///     proxy+server, trigger a mine, capture the client-leg 0x000B) is the §3
///     deliverable and is BLOCKED behind Phase K: a fresh character starts
///     docked at a starbase, mining requires being in space near an asteroid,
///     and the dock-&gt;space transition is under crash-bisection
///     (SectorStartAckTests documents this). So no live mine exists to capture
///     yet -- faking one would violate the no-fake-capture discipline.
///
/// Because this is a separate C# copy of the layout, it does NOT auto-break if
/// the proxy C++ drifts -- only the live harness can catch real proxy drift.
/// Its value is (1) documenting the fabrication contract as executable spec and
/// (2) guarding the Duration signed-u16 cap regression (the centerpiece below).
/// </summary>
public sealed class ProspectBeamFabricationSpecTests
{
    public ProspectBeamFabricationSpecTests() => AnsiPalette.Enabled = false;

    private const ushort BitmaskEffectTimeDuration = 0x0007; // EffectID|TimeStamp|Duration
    private const ushort ProspectEffectDescId = 0x00BF;
    private const int DurationCap = 32000; // matches Object::SendObjectToObjectEffectRL (ObjectClass.cpp:884-885)

    /// <summary>
    /// Build the exact 0x000B body bytes that UDPClient::StartProspecting emits
    /// for a START_PROSPECT, including the Duration cap. Layout (all little-endian):
    ///   u16   Bitmask = 0x0007
    ///   i32   GameID   = prospector
    ///   i32   TargetID = asteroid
    ///   u16   EffectDescID = 0x00BF
    ///   u8    Message null terminator (0)
    ///   i32   EffectID  (bit 0x01)
    ///   u32   TimeStamp (bit 0x02)
    ///   i16   Duration  (bit 0x04) = min(drainMs, 32000)
    /// </summary>
    private static byte[] FabricateBeamBody(int prospector, int asteroid, int effectUid, uint startTick, uint drainMs)
    {
        short duration = drainMs > DurationCap ? (short)DurationCap : (short)drainMs;

        var b = new byte[23];
        var s = b.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(0), BitmaskEffectTimeDuration);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(2), prospector);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(6), asteroid);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(10), ProspectEffectDescId);
        b[12] = 0; // Message NULL
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(13), effectUid);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(17), startTick);
        BinaryPrimitives.WriteInt16LittleEndian(s.Slice(21), duration);
        return b;
    }

    private static string Dump(byte[] body) => PacketRecord.Resolve(0x000B, body).DumpToString();

    [Fact]
    public void ProspectBeam_HasExpectedHeaderAndEffectFields_AndConsumesEveryByte()
    {
        var body = FabricateBeamBody(
            prospector: 0x000A1234, asteroid: 0x000B5678, effectUid: 0x0000ABCD,
            startTick: 0x11223344, drainMs: 5000);

        var dump = Dump(body);

        Assert.Contains("Bitmask", dump);
        Assert.Contains("0x0007", dump);
        Assert.Contains("EffectID|TimeStamp|Duration", dump);  // exactly the three bits, no more
        Assert.Contains("0x000A1234", dump);                   // GameID = prospector (beam source)
        Assert.Contains("0x000B5678", dump);                   // TargetID = asteroid (beam target)
        Assert.Contains("EffectDescID", dump);
        Assert.Contains("0x00BF", dump);                       // prospect/mining beam id
        Assert.Contains("0x0000ABCD", dump);                   // EffectID = effectUID
        Assert.Contains("0x11223344", dump);                   // TimeStamp = startTick
        Assert.Contains("Duration", dump);
        Assert.Contains("5000", dump);

        Assert.DoesNotContain("???", dump);                    // every fabricated byte decoded
        Assert.DoesNotContain("truncated", dump);
        Assert.DoesNotContain("runs past", dump);
    }

    [Fact]
    public void ProspectBeam_LongMine_DurationCappedToStaySignedPositive()
    {
        // A full ore stack drains for well over 32.7s. Without the cap, the
        // client (which reads Duration SIGNED) would see a negative value and
        // not render the beam. The cap keeps it positive and visible.
        const uint longDrainMs = 60000;
        var body = FabricateBeamBody(1, 2, 3, 0, longDrainMs);

        // Pin the wire byte directly: i16 LE at offset 21 must be 32000, not 60000
        // (which would wrap to -5536 as int16) and not (60000 & 0xFFFF) = -5536.
        short onWire = BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(21));
        Assert.Equal((short)DurationCap, onWire);
        Assert.True(onWire > 0, "capped Duration must stay positive so the client renders the beam");

        // And the production decoder reads it back as the same positive 32000.
        var dump = Dump(body);
        Assert.Contains("Duration", dump);
        Assert.Contains("32000", dump);
        Assert.DoesNotContain("???", dump);
    }

    [Fact]
    public void ProspectBeam_ExactCapBoundary_NotAltered()
    {
        // drainMs == cap passes through unchanged; cap+1 clamps.
        Assert.Equal((short)DurationCap,
            BinaryPrimitives.ReadInt16LittleEndian(FabricateBeamBody(1, 2, 3, 0, DurationCap).AsSpan(21)));
        Assert.Equal((short)DurationCap,
            BinaryPrimitives.ReadInt16LittleEndian(FabricateBeamBody(1, 2, 3, 0, DurationCap + 1).AsSpan(21)));
        // Just under the cap is untouched.
        Assert.Equal((short)(DurationCap - 1),
            BinaryPrimitives.ReadInt16LittleEndian(FabricateBeamBody(1, 2, 3, 0, DurationCap - 1).AsSpan(21)));
    }
}
