// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

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
    /// <summary>A nested "bare" sub-structure (its own flag bitmap, no GameID/len/version header).</summary>
    Nested,
}

/// <summary>One conditional field in an Aux structure, gated by a flag bit.</summary>
public sealed class AuxField
{
    public int      FlagNum { get; }
    public string   Name    { get; }
    public AuxKind  Kind    { get; }
    public AuxSchema? Schema { get; }  // for Kind == Nested

    public AuxField(int flagNum, string name, AuxKind kind, AuxSchema? schema = null)
    {
        FlagNum = flagNum; Name = name; Kind = kind; Schema = schema;
    }
}

/// <summary>
/// Schema for an AuxBase-derived structure. A structure serialises as:
///   [flag bitmap: FlagBytes raw bytes]
///   then, in ascending flagNum order, each field whose bit (FlagNum+4) is set.
/// A field's bit lives in byte (FlagNum+4)/8, mask 1 &lt;&lt; ((FlagNum+4)%8).
/// Top-level structures additionally carry a header before the flag bitmap:
///   [u32 GameID][u16 bodyLen = payloadLen-6][u8 version=1].
///
/// Two flavours:
///  - record:    Fields drives the walk.
///  - container: ContainerCount slots, each an instance of ContainerElement,
///               present iff bit (slot+4) is set in the flag bitmap.
/// </summary>
public sealed class AuxSchema
{
    public string         Name            { get; }
    public int            FlagBytes       { get; }
    public bool           HasHeader       { get; init; }
    public IReadOnlyList<AuxField> Fields  { get; }

    public bool           IsContainer     { get; }
    public int            ContainerCount  { get; }
    public AuxSchema?     ContainerElement{ get; }

    // record
    public AuxSchema(string name, int flagBytes, IReadOnlyList<AuxField> fields, bool hasHeader = false)
    {
        Name = name; FlagBytes = flagBytes; Fields = fields; HasHeader = hasHeader;
        IsContainer = false; ContainerCount = 0; ContainerElement = null;
    }

    // container
    public AuxSchema(string name, int flagBytes, int count, AuxSchema element, bool hasHeader = false)
    {
        Name = name; FlagBytes = flagBytes; Fields = System.Array.Empty<AuxField>();
        HasHeader = hasHeader;
        IsContainer = true; ContainerCount = count; ContainerElement = element;
    }
}
