// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System;
using System.Collections.Generic;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Phase AB §1 -- the part of the three-place (server/proxy/CLI) sync invariant
/// that this phase can enforce automatically in CI without a live stack: the
/// CLI must NEVER silently lose a decoder for an opcode in the proxy
/// FABRICATION band. That band is where drift bites hardest -- the server emits
/// a compact control opcode, the proxy expands it into client-facing packets,
/// and if the CLI quietly drops one decoder to <see cref="GenericRecord"/> we
/// lose the ability to byte-pin that fabrication.
///
/// This is NOT whole-protocol coverage (that is the Phase-T
/// CoverageRatchetTests' job, ramped against the live server). It pins exactly
/// the fabrication band documented in plans/27 §3a + plans/28: the compact
/// server-&gt;proxy sources and the client-facing targets the proxy fabricates.
/// Deleting or remapping any registry line in that band fails this test.
/// </summary>
public sealed class FabricationBandCoverageTests
{
    // (opcode, the record type it MUST resolve to). Pinning the exact type --
    // not merely "not Generic" -- also catches an accidental remap to the wrong
    // record.
    private static readonly IReadOnlyList<(ushort Opcode, Type Record)> Band = new[]
    {
        // Compact control band the server emits to the proxy (range-list).
        ((ushort)0x2012, typeof(StartProspectRecord)),        // START_PROSPECT
        ((ushort)0x2013, typeof(TractorOreRecord)),           // TRACTOR_ORE
        ((ushort)0x2014, typeof(LootItemRecord)),             // LOOT_ITEM
        ((ushort)0x2018, typeof(StaticObjectCreateRecord)),   // STATIC_OBJECT_CREATE
        ((ushort)0x2019, typeof(ResourceObjectCreateRecord)), // RESOURCE_OBJECT_CREATE
        // Client-facing targets the proxy fabricates from the band above.
        ((ushort)0x0004, typeof(CreateRecord)),               // CREATE
        ((ushort)0x0007, typeof(RemoveRecord)),               // REMOVE
        ((ushort)0x000B, typeof(ObjectToObjectEffectRecord)), // OBJECT_TO_OBJECT_EFFECT (the beam)
        ((ushort)0x000F, typeof(RemoveEffectRecord)),         // REMOVE_EFFECT
        ((ushort)0x001B, typeof(AuxDataRecord)),              // AUX_DATA (name / skill aux)
        ((ushort)0x0046, typeof(ComponentPositionalUpdateRecord)), // COMPONENT_POSITIONAL_UPDATE
    };

    [Fact]
    public void EveryFabricationBandOpcode_ResolvesToItsDedicatedRecord()
    {
        var empty = Array.Empty<byte>();
        var misses = new List<string>();

        foreach (var (opcode, expected) in Band)
        {
            var actual = PacketRecord.Resolve(opcode, empty).GetType();
            if (actual == typeof(GenericRecord))
                misses.Add($"0x{opcode:X4} fell back to GenericRecord (decoder lost)");
            else if (actual != expected)
                misses.Add($"0x{opcode:X4} resolved to {actual.Name}, expected {expected.Name}");
        }

        Assert.True(misses.Count == 0,
            "Fabrication-band decoder drift:\n  " + string.Join("\n  ", misses));
    }
}
