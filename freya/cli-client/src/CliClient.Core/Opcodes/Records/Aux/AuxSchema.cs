// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

namespace N7.CliClient.Opcodes.Records.Aux;

/// <summary>
/// Wire kinds for a single AuxBase-serialised field. Sizes follow the server's
/// AddData&lt;T&gt; (LE; long/ulong forced to 4 bytes) and AddString (u16 len +
/// chars, no NUL). See AuxBase.cpp / AuxBase.h.
/// </summary>
public enum AuxKind
{
    U8, U16, U32, S32, U64, F32, F64, Bool, Str,
    /// <summary>4 consecutive u32 (the "Availability[4]" pattern, one flag bit).</summary>
    Avail4,
    /// <summary>3 consecutive float (RGB tag-colour triple, one flag bit).</summary>
    Float3,
    /// <summary>Mounts slot: u32 Mount + s32 + s32 (12B inline, one flag bit).</summary>
    Mount3,
    /// <summary>A nested sub-structure (its own flag bitmap, no GameID/len/version header).</summary>
    Nested,
    /// <summary>Nested struct whose BuildPacket emits nothing (e.g. AuxDamage):
    /// present bit set => zero bytes; still honours the extended 0x05 marker.</summary>
    Empty,
}

/// <summary>One conditional field in an Aux structure, gated by a flag bit.</summary>
public sealed class AuxField
{
    public int      FlagNum { get; }
    public string   Name    { get; }
    public AuxKind  Kind    { get; }
    public AuxSchema? Schema { get; }  // for Kind == Nested

    /// <summary>
    /// True when the EXTENDED (BuildExtendedPacket) path has this nested field's
    /// full-serialise branch commented out in the server (e.g. AuxPlayerIndex
    /// Missions): in extended mode it emits only a 0x05 if its deleted-bit is
    /// set, never the full struct, and its present-bit is ignored. Plain mode
    /// is unaffected.
    /// </summary>
    public bool ExtSuppressed { get; }

    public AuxField(int flagNum, string name, AuxKind kind, AuxSchema? schema = null, bool extSuppressed = false)
    {
        FlagNum = flagNum; Name = name; Kind = kind; Schema = schema; ExtSuppressed = extSuppressed;
    }
}

/// <summary>
/// Schema for an AuxBase-derived structure. Serialised form:
///   [flag bitmap][conditional fields/slots in ascending flagNum order]
/// where a field/slot with flagNum N is present iff bit (N+4) is set in the
/// bitmap (byte (N+4)/8, mask 1&lt;&lt;((N+4)%8)).
///
/// FlagCount is the structure's m_FlagCount (== number of fields for a record,
/// == slot count for a container). Both flag-bitmap widths derive from it:
///   plain bitmap  = ceil((FlagCount+4)/8)        (BuildPacket)
///   extended      = ceil((2*FlagCount+4)/8)      (BuildExtendedPacket)
/// In the extended form a field also has a "deleted" bit at (N+4+FlagCount);
/// an absent-but-deleted nested field is replaced on the wire by a single 0x05.
///
/// Top-level structures additionally carry a header before the bitmap:
///   [u32 GameID][u16 bodyLen = payloadLen-6][u8 version=1].
/// </summary>
public sealed class AuxSchema
{
    public string         Name            { get; }
    public bool           HasHeader       { get; init; }
    public IReadOnlyList<AuxField> Fields  { get; }

    public bool           IsContainer     { get; }
    public int            ContainerCount  { get; }
    public AuxSchema?     ContainerElement{ get; }

    public int FlagCount     => IsContainer ? ContainerCount : Fields.Count;
    public int PlainFlagBytes => (FlagCount + 4 + 7) / 8;
    public int ExtFlagBytes   => (2 * FlagCount + 4 + 7) / 8;

    // record
    public AuxSchema(string name, IReadOnlyList<AuxField> fields, bool hasHeader = false)
    {
        Name = name; Fields = fields; HasHeader = hasHeader;
        IsContainer = false; ContainerCount = 0; ContainerElement = null;
    }

    // container
    public AuxSchema(string name, int count, AuxSchema element, bool hasHeader = false)
    {
        Name = name; Fields = System.Array.Empty<AuxField>(); HasHeader = hasHeader;
        IsContainer = true; ContainerCount = count; ContainerElement = element;
    }
}
