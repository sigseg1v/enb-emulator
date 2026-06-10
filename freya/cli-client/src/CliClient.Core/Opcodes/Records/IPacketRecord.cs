// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// One concrete <see cref="IPacketRecord"/> per opcode. Each implementation
/// owns:
///   1. the field layout (private record fields parsed off the wire)
///   2. <see cref="DumpToString"/> -- the human-skimmable multi-line dump
///      with named fields, suspicious-value flags, and (eventually) an
///      embedded hex tail
///   3. an awareness of which fields are "load-bearing for the client"
///      (names, ids, descriptions) so corruption is obvious in the dump
///
/// The same record is used by:
///   - the REPL `dump` command for live frames the server emits
///   - the same command's `--compare` mode, where retail frames loaded
///     from an ENBREPLAY capture are wrapped in records and dumped
///     side-by-side
///
/// Records are constructed via the static <see cref="PacketRecord.Resolve"/>
/// factory which dispatches on opcode through
/// <see cref="PacketRecordRegistry"/>.
/// </summary>
public interface IPacketRecord
{
    /// <summary>Opcode this record represents.</summary>
    ushort Opcode { get; }

    /// <summary>Length of the raw payload this record was parsed from.</summary>
    int PayloadLength { get; }

    /// <summary>
    /// Multi-line annotated dump. Lines start with two spaces of indent
    /// (so the caller can prefix a frame number on the header without
    /// the body collapsing visually). The full payload always appears
    /// in the trailing hex+ASCII gutter -- nothing is truncated, on the
    /// grounds that finding garbage values is the whole point of the
    /// dump. Includes ANSI colour codes when
    /// <see cref="N7.CliClient.Logging.AnsiPalette.Enabled"/> is true.
    /// </summary>
    string DumpToString();
}
