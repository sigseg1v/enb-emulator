// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System;
using System.Text;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins the 0x001B manufacturing-index aux for a manufacturing terminal,
/// comparing two payloads of the SAME index:
///
///   * <see cref="RetailManuAux"/> -- the correct tree. Under the "Items"
///     primary category there are exactly THREE sub-categories (Weapons, Systems,
///     Core).
///   * <see cref="OurPreFixManuAux"/> -- our server's tree BEFORE the fix. It
///     carries a fourth, non-retail category "Consumables" (with one entry
///     "Consumable X") under "Items".
///
/// Why this matters: the client builds an indexable category vector from this
/// tree. In modes above MANUFACTURE (Analyze = mode 2, vs Dock = mode 1) it walks
/// that vector and dereferences each element. The extra fourth category yields a
/// null slot that is only walked in Analyze mode -- which is exactly why docking
/// the terminal works but clicking Analyze crashes. The server fix deletes the
/// hardcoded "Consumables" category in AuxManufacturingIndex.cpp.
///
/// The two payloads are byte-identical everywhere except the divergence this
/// pins: (1) the "Items" category-list flag byte has the fourth-slot present bit
/// set in ours (0xf6) but clear in retail (0x76), and (2) ours splices in the
/// "Consumables" sub-tree where retail emits a single 0x05 deletion marker.
/// <see cref="Fix_RemovesConsumables_YieldsRetailBytes"/> proves that undoing
/// exactly those two things reproduces the retail body byte-for-byte.
///
/// Note: the deeply nested category/sub-category flag encoding (AuxBase 2-bit
/// extended form, e.g. Core's 0x563e sub-category flags) is byte-identical
/// between our server and retail here, so it is not part of the divergence and is
/// not re-derived in this test.
/// </summary>
public sealed class AuxManufacturingIndexWalkTests
{
    // Aux sub-packet payload bytes only (GameID + BodyLen header + body). GameID
    // and BodyLen differ between the two (different session / sizes); the body is
    // what carries the structural divergence.
    private const string RetailManuAux =
        "309903c0ea0201364070f9ff1111004d616e75666163747572696e67204c616201000000050505f6b605004974656d73763e76010700576561706f6e73f63e360204004265616d6400000036020a0050726f6a656374696c6565000000360207004d697373696c656600000036020400416d6d6f67000000050a0000007601070053797374656d73363e3602050042617369636e0000003602050044726f6e656f0000000505050b00000076010400436f7265563e3602070052656163746f727800000036020600456e67696e657900000036020600536869656c647a00000005050c0000000505b60a00436f6d706f6e656e7473f63f76011600436f6d7075746572202f20456c656374726f6e696373763e36020800536f6674776172658c00000036020b00456c656374726f6e6963738d00000036020800436f6d70757465728e00000005053200000076010500506f776572f63e36020f00506f77657220436f6e7665727465729600000036020e00506f77657220436f75706c696e679700000036020a00506f77657220436f7265980000003602090047656e657261746f7299000000053300000076010a0046616272696361746564f63e36020f00536869656c64656420436173696e67a000000036020c00456e67696e65204672616d65a100000036020b0044726f6e65204672616d65a2000000360206004d6f756e7473a3000000053400000076011100576561706f6e20436f6d706f6e656e7473f63e36021000466972696e67204d656368616e69736daa00000036021400416d6d6f20466565646572202f204c6f61646572ab000000360206004f7074696373ac0000003602140042617272656c202f204c61756e63682054756265ad000000053500000076010400416d6d6ff63e36020b00536c7567202f205261696cb40000003602070057617268656164b500000036020a0050726f70656c6c616e74b600000036020c005368656c6c20436173696e67b7000000053600000005050000803f0000803f0000803fff010000";

    private const string OurPreFixManuAux =
        "270000c0160301364070f9ff1111004d616e75666163747572696e67204c616201000000050505f6b605004974656d73f63e76010700576561706f6e73f63e360204004265616d6400000036020a0050726f6a656374696c6565000000360207004d697373696c656600000036020400416d6d6f67000000050a0000007601070053797374656d73363e3602050042617369636e0000003602050044726f6e656f0000000505050b00000076010400436f7265563e3602070052656163746f727800000036020600456e67696e657900000036020600536869656c647a00000005050c00000076010b00436f6e73756d61626c6573163e36020c00436f6e73756d61626c65205882000000050505050d00000005b60a00436f6d706f6e656e7473f63f76011600436f6d7075746572202f20456c656374726f6e696373763e36020800536f6674776172658c00000036020b00456c656374726f6e6963738d00000036020800436f6d70757465728e00000005053200000076010500506f776572f63e36020f00506f77657220436f6e7665727465729600000036020e00506f77657220436f75706c696e679700000036020a00506f77657220436f7265980000003602090047656e657261746f7299000000053300000076010a0046616272696361746564f63e36020f00536869656c64656420436173696e67a000000036020c00456e67696e65204672616d65a100000036020b0044726f6e65204672616d65a2000000360206004d6f756e7473a3000000053400000076011100576561706f6e20436f6d706f6e656e7473f63e36021000466972696e67204d656368616e69736daa00000036021400416d6d6f20466565646572202f204c6f61646572ab000000360206004f7074696373ac0000003602140042617272656c202f204c61756e63682054756265ad000000053500000076010400416d6d6ff63e36020b00536c7567202f205261696cb40000003602070057617268656164b500000036020a0050726f70656c6c616e74b600000036020c005368656c6c20436173696e67b7000000053600000005050000803f0000803f0000803fff010000";

    // The non-retail "Consumables" category sub-tree our server splices into the
    // "Items" category list (one category "Consumables" holding one sub-category
    // "Consumable X"). The fix removes it; retail emits a single 0x05 marker here.
    private const string ConsumablesSubTree =
        "76010b00436f6e73756d61626c6573163e36020c00436f6e73756d61626c65205882000000050505050d000000";

    private const int HeaderLen = 6; // u32 GameID + u16 BodyLen

    private static byte[] Body(string hex) => Convert.FromHexString(hex)[HeaderLen..];
    private static int IndexOf(byte[] hay, ReadOnlySpan<byte> needle)
    {
        for (int i = 0; i + needle.Length <= hay.Length; i++)
            if (hay.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }

    [Fact]
    public void OnlyDivergence_IsTheConsumablesCategory()
    {
        byte[] ret = Body(RetailManuAux);
        byte[] our = Body(OurPreFixManuAux);

        // Identical up through the "Items" category name; the very next byte is
        // the Items category-list flag, which is where they first differ.
        int items = IndexOf(ret, "Items"u8);
        Assert.True(items > 0);
        Assert.Equal(IndexOf(our, "Items"u8), items);
        int flag = items + "Items"u8.Length;
        Assert.Equal(ret.AsSpan(0, flag).ToArray(), our.AsSpan(0, flag).ToArray());

        // The only flag-byte difference is the fourth-slot present bit (0x80):
        // retail 0x76 (3 categories), ours 0xf6 (4th category present).
        Assert.Equal(0x76, ret[flag]);
        Assert.Equal(0xf6, our[flag]);
        Assert.Equal(0x80, our[flag] ^ ret[flag]);

        // The "Consumables" sub-tree is present in ours, absent from retail.
        Assert.True(IndexOf(our, Convert.FromHexString(ConsumablesSubTree)) > flag);
        Assert.Equal(-1, IndexOf(ret, Convert.FromHexString(ConsumablesSubTree)));
        Assert.Equal(-1, IndexOf(ret, "Consumable"u8));
    }

    [Fact]
    public void Fix_RemovesConsumables_YieldsRetailBytes()
    {
        byte[] ret = Body(RetailManuAux);
        byte[] our = Body(OurPreFixManuAux);

        // Apply exactly the server fix's wire effect to our pre-fix body:
        //   (1) replace the spliced Consumables sub-tree with the single 0x05
        //       deletion marker retail emits for that absent category slot, and
        //   (2) clear the fourth-slot present bit in the Items category flag
        //       (0xf6 -> 0x76).
        // The result must equal the retail body byte-for-byte -- i.e. removing
        // the hardcoded category makes our 0x001B aux identical to retail.
        var consumables = Convert.FromHexString(ConsumablesSubTree);
        int at = IndexOf(our, consumables);
        Assert.True(at > 0);

        var rebuilt = new byte[our.Length - consumables.Length + 1];
        our.AsSpan(0, at).CopyTo(rebuilt);
        rebuilt[at] = 0x05;
        our.AsSpan(at + consumables.Length).CopyTo(rebuilt.AsSpan(at + 1));

        int flag = IndexOf(rebuilt, "Items"u8) + "Items"u8.Length;
        rebuilt[flag] &= 0x7f; // clear the fourth-slot present bit

        Assert.Equal(ret, rebuilt);
    }
}
