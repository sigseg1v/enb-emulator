// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Buffers.Binary;

namespace N7.CliClient.Opcodes.Inbound;

/// <summary>
/// 0x0001 VERSION_RESPONSE — server reply to a
/// <see cref="Outbound.VersionRequest"/>. Single int32 status:
/// 0 = version OK, 1 = client too old, 2 = client newer than server.
/// </summary>
/// <remarks>
/// Wire format: one 4-byte status in HOST byte order (little-endian on
/// every architecture we care about). The server-side <c>SendResponse</c>
/// ships <c>&amp;status</c> as raw bytes with no <c>htonl</c> wrap —
/// see <c>proxy/ClientToServer_linux_stubs.cpp:58-59</c>. The Win32
/// path uses <c>long status</c> which is 4 bytes on Win32 LP32; the
/// Linux stub uses <c>int32_t</c> explicitly to match the wire size
/// across platforms.
/// </remarks>
public sealed record VersionResponse(int Status)
{
    public bool ClientUpToDate => Status == 0;
    public bool ClientTooOld   => Status == 1;
    public bool ClientNewer    => Status == 2;
}

/// <summary>Codec for opcode 0x0001 VERSION_RESPONSE (server → client).</summary>
public sealed class VersionResponseCodec : IOpcodeCodec
{
    public const int WireSize = 4;

    public OpcodeId Opcode => OpcodeId.Known.VersionResponse;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < WireSize)
            throw new InvalidDataException(
                $"VersionResponse payload is {payload.Length} bytes, expected {WireSize}");

        // Host byte order on the wire — server doesn't htonl the status.
        // Every platform we ship to is little-endian, so reading LE is
        // the only correct call here; a big-endian server would be a
        // new project altogether.
        int status = BinaryPrimitives.ReadInt32LittleEndian(payload[0..4]);
        return new VersionResponse(status);
    }

    public byte[] EncodeOutbound(object message)
        => throw new NotSupportedException(
            "VersionResponse is server-to-client only; the client never originates it");
}
