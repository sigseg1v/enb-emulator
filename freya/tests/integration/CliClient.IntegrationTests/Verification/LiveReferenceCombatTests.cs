// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.IntegrationTests.Verification;

/// <summary>
/// Phase AF (plans/33, AF-7) -- byte-pins for the combat band
/// (0x000B OBJECT_TO_OBJECT_EFFECT, 0x000E OBJECT_TO_OBJECT_LINKED_EFFECT,
/// 0x008B ATTACKER_UPDATES) against frames captured from the LIVE Net-7
/// reference server, cleartext proxy&lt;-&gt;server UDP leg.
///
/// <para>
/// 0x008B carries the lone byte-swapped field in this corpus: its MobId is
/// emitted BIG-ENDIAN on the wire. The CLI record (and the server emitter,
/// PlayerConnection.cpp:3604-3620) originally treated it little-endian -- a
/// Trap-1 (CLAUDE.md "Wire format &amp; byte order") defect that would have fed
/// the real client a byte-reversed attacker id. These fixtures pin the correct
/// big-endian decode and are the primary-source proof for the matching server
/// fix (plans/29 CV entry).
/// </para>
/// </summary>
public sealed class LiveReferenceCombatTests
{
    public LiveReferenceCombatTests() => AnsiPalette.Enabled = false;

    private static string DecodeClean(ushort opcode, string fixture, int expectLen, System.Type recordType)
    {
        byte[] b = HexFixture.Load(fixture);
        Assert.Equal(expectLen, b.Length);
        var rec = PacketRecord.Resolve(opcode, b);
        Assert.IsType(recordType, rec);
        string dump = rec.DumpToString();
        Assert.DoesNotContain("???", dump);   // every byte decoded
        Assert.DoesNotContain("[!]", dump);   // no truncation / overrun flag
        return dump;
    }

    [Fact]
    public void ObjectEffect_0x000B_WeaponBeam_LiveCapture()
    {
        byte[] b = HexFixture.Load("live_object_effect_000B_weapon.hex");
        Assert.Equal(35, b.Length);

        Assert.Equal((short)0x0007,               BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(0)));
        Assert.Equal(unchecked((int)0x4003992A),  BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(2)));
        Assert.Equal(0x000186F5,                  BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(6)));

        string d = DecodeClean(0x000B, "live_object_effect_000B_weapon.hex", 35, typeof(ObjectToObjectEffectRecord));
        Assert.Contains("~02/~WEAP_02", d);
    }

    [Fact]
    public void ObjectLinkedEffect_0x000E_LiveCapture()
    {
        byte[] b = HexFixture.Load("live_object_linked_effect_000E.hex");
        Assert.Equal(58, b.Length);

        Assert.Equal(0x0025417A,                 BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0)));
        Assert.Equal(unchecked((int)0x4003992A), BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8)));
        Assert.Equal(0x000186F5,                 BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(13)));

        DecodeClean(0x000E, "live_object_linked_effect_000E.hex", 58, typeof(ObjectToObjectLinkedEffectRecord));
    }

    // 0x008B: MobId is BIG-ENDIAN on the wire. The fixtures' trailing 4 bytes
    // are the big-endian encoding of the mob id; reading them little-endian
    // (the original record + server bug) yields a byte-reversed nonsense id.
    [Theory]
    [InlineData("live_attacker_updates_008B_start.hex", 1, 0x000187EC)]
    [InlineData("live_attacker_updates_008B_stop.hex",  0, 0x000186F5)]
    public void AttackerUpdates_0x008B_MobIdIsBigEndian_LiveCapture(string fixture, int update, int mobId)
    {
        byte[] b = HexFixture.Load(fixture);
        Assert.Equal(9, b.Length);

        Assert.Equal(update, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0)));  // Update is LE
        Assert.Equal(0x01, b[4]);                                                   // Fixed
        Assert.Equal(mobId, BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(5)));      // MobId is BE
        // Sanity: little-endian read would NOT recover the mob id.
        Assert.NotEqual(mobId, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(5)));

        string d = DecodeClean(0x008B, fixture, 9, typeof(AttackerUpdatesRecord));
        Assert.Contains($"0x{mobId:X8}", d);
    }
}
