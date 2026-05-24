// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

public sealed class OpcodeRegistryTests
{
    private sealed class FakeCodec : IOpcodeCodec
    {
        public OpcodeId Opcode { get; }
        public FakeCodec(OpcodeId opcode) { Opcode = opcode; }
        public object DecodeInbound(ReadOnlySpan<byte> payload) => payload.ToArray();
        public byte[] EncodeOutbound(object message) => (byte[])message;
    }

    [Fact]
    public void Register_AndResolve_ReturnsTheSameCodec()
    {
        var registry = new OpcodeRegistry();
        var codec = new FakeCodec(OpcodeId.Known.MasterJoin);

        registry.Register(codec);

        Assert.Same(codec, registry.Resolve(OpcodeId.Known.MasterJoin));
        Assert.True(registry.IsRegistered(OpcodeId.Known.MasterJoin));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Resolve_UnregisteredOpcode_ReturnsUnknownCodec()
    {
        var registry = new OpcodeRegistry();
        var opcode = new OpcodeId(0xBEEF);

        var resolved = registry.Resolve(opcode);

        var unknown = Assert.IsType<UnknownOpcodeCodec>(resolved);
        Assert.Equal(opcode, unknown.Opcode);
        Assert.False(registry.IsRegistered(opcode));
    }

    [Fact]
    public void UnknownCodec_DecodeInbound_ReturnsPayloadCopy()
    {
        var codec = new UnknownOpcodeCodec(new OpcodeId(0x1234));
        byte[] payload = new byte[] { 1, 2, 3 };

        var decoded = codec.DecodeInbound(payload);

        var result = Assert.IsType<UnknownOpcodePayload>(decoded);
        Assert.Equal(new OpcodeId(0x1234), result.Opcode);
        Assert.Equal(payload, result.RawPayload);
    }

    [Fact]
    public void UnknownCodec_EncodeOutbound_Throws()
    {
        var codec = new UnknownOpcodeCodec(new OpcodeId(0x1234));
        Assert.Throws<NotSupportedException>(() => codec.EncodeOutbound(new object()));
    }

    [Fact]
    public void Register_LastWriterWins()
    {
        var registry = new OpcodeRegistry();
        var first = new FakeCodec(OpcodeId.Known.Login);
        var second = new FakeCodec(OpcodeId.Known.Login);

        registry.Register(first);
        registry.Register(second);

        Assert.Same(second, registry.Resolve(OpcodeId.Known.Login));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Register_NullCodec_Throws()
    {
        var registry = new OpcodeRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void RegisteredOpcodes_ReflectsRegistrations()
    {
        var registry = new OpcodeRegistry();
        registry.Register(new FakeCodec(OpcodeId.Known.MasterJoin));
        registry.Register(new FakeCodec(OpcodeId.Known.Login));

        var opcodes = registry.RegisteredOpcodes;

        Assert.Equal(2, opcodes.Count);
        Assert.Contains(OpcodeId.Known.MasterJoin, opcodes);
        Assert.Contains(OpcodeId.Known.Login, opcodes);
    }

    [Fact]
    public void OpcodeId_ToString_IsHexFormatted()
    {
        Assert.Equal("0x0035", OpcodeId.Known.MasterJoin.ToString());
        Assert.Equal("0x0002", OpcodeId.Known.Login.ToString());
    }
}
