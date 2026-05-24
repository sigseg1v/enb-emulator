// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Tests for the generated <see cref="OpcodeNames"/> table and the
/// <see cref="NamedOpaqueCodec"/> / <see cref="OpcodeRegistry.RegisterAllNamedOpaque"/>
/// bulk-registration path introduced in Phase S Item 15.
/// </summary>
public sealed class OpcodeNamesTests
{
    [Fact]
    public void All_ContainsExpectedCount()
    {
        // Opcodes.h declares 209 #defines today; two collapse to one
        // entry each because the same hex value carries two aliases
        // (0x2010 SET_GLOBAL_LOGIN_LINK/DATA_FILE,
        //  0x2011 SET_PROXY_SECTOR_LINK/GALAXY_MAP_CACHE).
        // Net 207 distinct opcodes. If this changes, regenerate the table
        // (scripts/generate-opcode-names.sh) and update this assertion.
        Assert.Equal(207, OpcodeNames.Count);
        Assert.Equal(207, OpcodeNames.All.Count);
    }

    [Theory]
    [InlineData(0x0033, "CLIENT_CHAT")]
    [InlineData(0x0035, "MASTER_JOIN")]
    [InlineData(0x0036, "SERVER_REDIRECT")]
    [InlineData(0x006D, "GLOBAL_CONNECT")]
    [InlineData(0x0070, "GLOBAL_AVATAR_LIST")]
    [InlineData(0x003A, "SERVER_HANDOFF")]
    public void Get_KnownOpcodes_ReturnsCanonicalName(ushort opcode, string expected)
    {
        Assert.Equal(expected, OpcodeNames.Get(new OpcodeId(opcode)));
    }

    [Fact]
    public void Get_UnknownOpcode_ReturnsUnknown()
    {
        Assert.Equal("UNKNOWN", OpcodeNames.Get(new OpcodeId(0xFFFE)));
    }

    [Fact]
    public void Format_RendersHexAndName()
    {
        Assert.Equal("0x0033 CLIENT_CHAT",
            OpcodeNames.Format(new OpcodeId(0x0033)));
    }

    [Fact]
    public void DuplicateOpcodeAliases_AreCollapsed_WithOrJoin()
    {
        // The generator collapses duplicate-hex entries to NAME_A_OR_NAME_B.
        // Document the two cases as fixed facts — if either changes the
        // generator's dup logic also needs a look.
        Assert.Equal("SET_GLOBAL_LOGIN_LINK_OR_DATA_FILE",
            OpcodeNames.Get(new OpcodeId(0x2010)));
        Assert.Equal("SET_PROXY_SECTOR_LINK_OR_GALAXY_MAP_CACHE",
            OpcodeNames.Get(new OpcodeId(0x2011)));
    }
}

public sealed class NamedOpaqueCodecTests
{
    [Fact]
    public void DecodeInbound_ReturnsNamedOpaquePayload()
    {
        var codec = new NamedOpaqueCodec(new OpcodeId(0x00CE), "GUILD_REQUEST_CHANGE");
        byte[] payload = { 1, 2, 3, 4 };

        var result = (NamedOpaquePayload)codec.DecodeInbound(payload);

        Assert.Equal(new OpcodeId(0x00CE), result.Opcode);
        Assert.Equal("GUILD_REQUEST_CHANGE", result.Name);
        Assert.Equal(payload, result.RawPayload);
    }

    [Fact]
    public void DecodeInbound_CopiesPayload()
    {
        // Defensive: caller's span must not alias the payload buffer.
        var codec = new NamedOpaqueCodec(new OpcodeId(0x0001), "TEST");
        byte[] src = { 0xAA, 0xBB };
        var result = (NamedOpaquePayload)codec.DecodeInbound(src);
        src[0] = 0xFF;
        Assert.Equal(0xAA, result.RawPayload[0]);
    }

    [Fact]
    public void EncodeOutbound_Throws()
    {
        var codec = new NamedOpaqueCodec(new OpcodeId(0x0001), "TEST");
        Assert.Throws<NotSupportedException>(
            () => codec.EncodeOutbound(new byte[0]));
    }

    [Fact]
    public void Constructor_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(
            () => new NamedOpaqueCodec(new OpcodeId(0x0001), ""));
    }
}

public sealed class OpcodeRegistryBulkRegistrationTests
{
    [Fact]
    public void RegisterAllNamedOpaque_PopulatesEveryKnownOpcode()
    {
        var registry = new OpcodeRegistry();
        int added = registry.RegisterAllNamedOpaque();

        Assert.Equal(OpcodeNames.Count, added);
        Assert.Equal(OpcodeNames.Count, registry.Count);

        foreach (var (value, _) in OpcodeNames.All)
        {
            Assert.True(registry.IsRegistered(new OpcodeId(value)),
                $"opcode 0x{value:X4} should be registered after bulk-opaque pass");
        }
    }

    [Fact]
    public void RegisterAllNamedOpaque_DoesNotOverwriteTypedCodec()
    {
        // The typed ClientChatCodec wins regardless of registration order.
        var registry = new OpcodeRegistry();
        var typed = new N7.CliClient.Opcodes.Outbound.ClientChatCodec();

        registry.Register(typed);
        registry.RegisterAllNamedOpaque();

        var resolved = registry.Resolve(typed.Opcode);
        Assert.Same(typed, resolved);
    }

    [Fact]
    public void RegisterAllNamedOpaque_LeavesTypedCodecAlone_WhenCalledFirst()
    {
        // And the reverse order works too — typed Register() over an
        // existing opaque overwrites correctly.
        var registry = new OpcodeRegistry();
        registry.RegisterAllNamedOpaque();

        var typed = new N7.CliClient.Opcodes.Outbound.ClientChatCodec();
        registry.Register(typed);

        var resolved = registry.Resolve(typed.Opcode);
        Assert.Same(typed, resolved);
    }

    [Fact]
    public void RegisterAllNamedOpaque_IsIdempotent()
    {
        var registry = new OpcodeRegistry();
        int first = registry.RegisterAllNamedOpaque();
        int second = registry.RegisterAllNamedOpaque();

        Assert.Equal(OpcodeNames.Count, first);
        Assert.Equal(0, second);
        Assert.Equal(OpcodeNames.Count, registry.Count);
    }

    [Fact]
    public void Resolve_AfterBulkRegistration_ReturnsNamedOpaqueForUntypedOpcode()
    {
        var registry = new OpcodeRegistry();
        registry.RegisterAllNamedOpaque();

        // 0x008C LOOT_HULK_PERMISSION has no typed codec yet.
        var resolved = registry.Resolve(new OpcodeId(0x008C));
        var opaque = Assert.IsType<NamedOpaqueCodec>(resolved);
        Assert.Equal("LOOT_HULK_PERMISSION", opaque.Name);
    }

    [Fact]
    public void Resolve_AfterBulkRegistration_FallsBackToUnknown_ForOpcodesOutsideOpcodesH()
    {
        // 0xFFFE isn't in Opcodes.h, so even after bulk-opaque the
        // truly-unknown fallback still applies. Verifies the layering.
        var registry = new OpcodeRegistry();
        registry.RegisterAllNamedOpaque();

        var resolved = registry.Resolve(new OpcodeId(0xFFFE));
        Assert.IsType<UnknownOpcodeCodec>(resolved);
    }
}
