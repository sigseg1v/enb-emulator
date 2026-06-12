// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x2011 GALAXY_MAP_CACHE. Zero-length marker; the server uses it to tell the
/// client its cached galaxy-map data for this sector is current. No payload.
/// </summary>
public sealed class GalaxyMapCacheRecord : PacketRecord
{
    public GalaxyMapCacheRecord(ReadOnlySpan<byte> payload) : base(0x2011, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length != 0) Flag(sb, $"GALAXY_MAP_CACHE expected 0 bytes, got {Payload.Length}");
    }
}
