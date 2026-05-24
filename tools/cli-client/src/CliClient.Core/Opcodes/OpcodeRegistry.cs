// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Collections.Concurrent;

namespace N7.CliClient.Opcodes;

/// <summary>
/// Central opcode → codec lookup. Adding a new opcode is one
/// <see cref="Register"/> call (typically from a static
/// <c>ctor</c> on the codec itself) plus the codec file in
/// <c>Opcodes/Inbound/</c> or <c>Opcodes/Outbound/</c> — no central
/// switch statement.
/// </summary>
/// <remarks>
/// <para>
/// Thread-safe: registry mutations are serialised on an internal lock,
/// and reads are O(1) lock-free via <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// </para>
/// <para>
/// Per plans/19-phase-s-cli-client.md, unknown opcodes are NEVER an
/// error — they get logged as warnings and returned as
/// <see cref="UnknownOpcodePayload"/>. This keeps capture-replay tests
/// from breaking when Phase K wires server-side handlers ahead of CLI
/// client decoders.
/// </para>
/// </remarks>
public sealed class OpcodeRegistry
{
    private readonly ConcurrentDictionary<ushort, IOpcodeCodec> _codecs = new();

    /// <summary>
    /// Register a codec for an opcode. Replaces any prior registration
    /// for the same opcode (last-writer-wins; intentional so tests can
    /// swap codecs out without restarting the registry).
    /// </summary>
    public void Register(IOpcodeCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _codecs[codec.Opcode.Value] = codec;
    }

    /// <summary>
    /// Resolve the codec for an opcode. Returns a fresh
    /// <see cref="UnknownOpcodeCodec"/> if nothing is registered — never
    /// returns null.
    /// </summary>
    public IOpcodeCodec Resolve(OpcodeId opcode)
        => _codecs.TryGetValue(opcode.Value, out var codec)
            ? codec
            : new UnknownOpcodeCodec(opcode);

    /// <summary>True if a (non-fallback) codec is registered for this opcode.</summary>
    public bool IsRegistered(OpcodeId opcode) => _codecs.ContainsKey(opcode.Value);

    /// <summary>How many opcodes have explicit codecs registered.</summary>
    public int Count => _codecs.Count;

    /// <summary>Every registered opcode (snapshot; safe to iterate).</summary>
    public IReadOnlyCollection<OpcodeId> RegisteredOpcodes
        => _codecs.Keys.Select(v => new OpcodeId(v)).ToArray();

    /// <summary>
    /// Pre-populate the registry with a <see cref="NamedOpaqueCodec"/>
    /// for every opcode in <see cref="OpcodeNames.All"/>. Existing
    /// registrations are NOT overwritten — so call this before *or*
    /// after registering typed codecs and the typed ones always win.
    /// </summary>
    /// <returns>Number of opaque codecs newly added.</returns>
    /// <remarks>
    /// Phase S Item 15 deliverable: after one call to this method, every
    /// opcode in Opcodes.h has a codec, the packet log carries names for
    /// every frame, and the UnknownOpcodeCodec fallback only fires for
    /// opcodes outside Opcodes.h (which the real client never emits).
    /// </remarks>
    public int RegisterAllNamedOpaque()
    {
        int added = 0;
        foreach (var (value, name) in OpcodeNames.All)
        {
            var id = new OpcodeId(value);
            if (_codecs.TryAdd(value, new NamedOpaqueCodec(id, name)))
                added++;
        }
        return added;
    }
}
