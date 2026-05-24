// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Net;

namespace N7.CliClient.Opcodes;

/// <summary>
/// Per-opcode encoder/decoder pair. One implementation per opcode the
/// server can send or accept. The registry dispatches to these by
/// <see cref="OpcodeId"/>.
/// </summary>
/// <remarks>
/// Both directions live on one interface because almost every opcode is
/// bidirectional in practice (the same payload shape, just the side that
/// originates varies). When an opcode is one-direction-only, the unused
/// half throws <see cref="NotSupportedException"/>.
/// </remarks>
public interface IOpcodeCodec
{
    /// <summary>The opcode this codec handles.</summary>
    OpcodeId Opcode { get; }

    /// <summary>
    /// Decode a server-to-client payload into a strongly-typed object.
    /// Implementations should return an immutable record / class. The
    /// caller hands the decoded object to the workflow / event bus.
    /// </summary>
    /// <param name="payload">The opcode-specific bytes (header already
    /// stripped). May be empty for opcodes that carry no body.</param>
    object DecodeInbound(ReadOnlySpan<byte> payload);

    /// <summary>
    /// Encode a strongly-typed client-to-server message into raw payload
    /// bytes. The codec produces only the payload; the framing wrapper
    /// (size+opcode) is added by <see cref="Packet.ForOpcode"/>.
    /// </summary>
    byte[] EncodeOutbound(object message);
}

/// <summary>
/// Default fallback for opcodes we know exist but haven't fleshed out
/// yet. The registry hands every unregistered opcode to this codec so
/// the packet log can record "saw 0xNNNN with N bytes" rather than
/// throw. Per plans/19-phase-s-cli-client.md, the goal is "no unknown
/// opcode warnings for any well-formed server traffic" — UnknownCodec
/// emits structured warnings instead of crashes.
/// </summary>
public sealed class UnknownOpcodeCodec : IOpcodeCodec
{
    public OpcodeId Opcode { get; }

    public UnknownOpcodeCodec(OpcodeId opcode) { Opcode = opcode; }

    public object DecodeInbound(ReadOnlySpan<byte> payload)
        => new UnknownOpcodePayload(Opcode, payload.ToArray());

    public byte[] EncodeOutbound(object message)
        => throw new NotSupportedException(
            $"opcode {Opcode} has no outbound encoder registered");
}

/// <summary>Result of <see cref="UnknownOpcodeCodec.DecodeInbound"/>.</summary>
public sealed record UnknownOpcodePayload(OpcodeId Opcode, byte[] RawPayload);

/// <summary>
/// A codec for an opcode we KNOW exists (it's in Opcodes.h) but
/// haven't written a typed decoder for yet. Same behaviour as
/// <see cref="UnknownOpcodeCodec"/> — emits raw payload bytes — but
/// carries the upstream symbolic name so the packet log can render
/// "0x00CE GUILD_GROUP_REQUEST_CHANGE: 12 bytes" instead of "UNKNOWN".
///
/// Used by <see cref="OpcodeRegistry.RegisterAllNamedOpaque"/> to
/// pre-populate the registry with one entry per opcode in Opcodes.h.
/// Real typed codecs (ClientChatCodec, MasterJoinCodec, ...) replace
/// these via the registry's last-writer-wins semantics.
/// </summary>
public sealed class NamedOpaqueCodec : IOpcodeCodec
{
    public OpcodeId Opcode { get; }
    public string Name { get; }

    public NamedOpaqueCodec(OpcodeId opcode, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Opcode = opcode;
        Name = name;
    }

    public object DecodeInbound(ReadOnlySpan<byte> payload)
        => new NamedOpaquePayload(Opcode, Name, payload.ToArray());

    public byte[] EncodeOutbound(object message)
        => throw new NotSupportedException(
            $"opcode {Opcode} ({Name}) has no typed encoder — only a name-tagged opaque codec is registered");
}

/// <summary>Result of <see cref="NamedOpaqueCodec.DecodeInbound"/>.</summary>
public sealed record NamedOpaquePayload(OpcodeId Opcode, string Name, byte[] RawPayload);
