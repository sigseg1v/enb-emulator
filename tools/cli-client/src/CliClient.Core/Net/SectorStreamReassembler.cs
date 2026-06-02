// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;

namespace N7.CliClient.Net;

/// <summary>
/// Reassembles the downstream MVAS/sector byte stream into complete inner
/// frames. Pure wire logic, deliberately split out of <see cref="SectorUdpClient"/>
/// so it can be byte-pinned without a live socket.
/// </summary>
/// <remarks>
/// <para>
/// Each datagram is a 12-byte <c>EnbUdpHeader</c> whose opcode is one of:
/// </para>
/// <list type="bullet">
/// <item><c>0x2016 PACKET_SEQUENCE</c> -- a batch that STARTS on an inner-frame
/// boundary; receiving one (re)establishes alignment.</item>
/// <item><c>0x201A PACKET_C_SEQUENCE</c> -- a continuation chunk of a frame that
/// was split across datagrams; only meaningful once aligned.</item>
/// <item><c>0x1007 TOGGLE_SEND_FREQ</c> -- carries the server-suggested send
/// rate (Hz) in its first 4 payload bytes.</item>
/// </list>
/// <para>
/// The 0x2016/0x201A payloads concatenate into one continuous byte stream of
/// <c>EnbTcpHeader { ushort size; ushort opcode }</c> frames (size = total
/// frame length). We emit each complete inner frame. A datagram drop leaves a
/// torn frame whose decoded <c>size</c> is implausible; on that signal we drop
/// the buffered partial and de-align, waiting for the next 0x2016 boundary
/// rather than emitting garbage.
/// </para>
/// </remarks>
public sealed class SectorStreamReassembler
{
    private const int HeaderSize = 12;            // EnbUdpHeader
    private const ushort PacketSequence  = 0x2016;
    private const ushort PacketCSequence = 0x201A;
    private const ushort ToggleSendFreq  = 0x1007;

    private static readonly IReadOnlyList<Packet> None = Array.Empty<Packet>();

    private readonly List<byte> _stream = new();
    private readonly Action<string>? _log;
    private bool _aligned;

    public SectorStreamReassembler(Action<string>? log = null) => _log = log;

    /// <summary>Server-suggested send rate (Hz) from the last 0x1007; default 20.</summary>
    public int Frequency { get; private set; } = 20;

    /// <summary>Whether we have hit a 0x2016 frame boundary and are emitting.</summary>
    public bool Aligned => _aligned;

    /// <summary>
    /// Feed one raw datagram (12-byte UDP header included); returns the inner
    /// frames it completed, in order. Returns an empty list for control
    /// datagrams, pre-alignment continuations, or partial frames.
    /// </summary>
    public IReadOnlyList<Packet> Push(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < HeaderSize) return None;
        ushort opcode = BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(2, 2));
        ReadOnlySpan<byte> payload = datagram[HeaderSize..];

        switch (opcode)
        {
            case PacketSequence:
                _aligned = true;                  // 0x2016 starts on a frame boundary
                return Drain(payload);
            case PacketCSequence:
                return _aligned ? Drain(payload) : None;
            case ToggleSendFreq:
                if (payload.Length >= 4)
                    Frequency = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(payload[..4]), 1, 60);
                return None;
            default:
                // 0x2010/0x201B/etc -- not needed for the world model.
                return None;
        }
    }

    private IReadOnlyList<Packet> Drain(ReadOnlySpan<byte> payload)
    {
        foreach (byte b in payload) _stream.Add(b);

        List<Packet>? frames = null;
        while (_stream.Count >= PacketHeader.WireSize)
        {
            int size  = _stream[0] | (_stream[1] << 8);
            ushort op = (ushort)(_stream[2] | (_stream[3] << 8));
            if (size < PacketHeader.WireSize || size > 65535)
            {
                // Desync (likely a dropped datagram). Resync at the next
                // 0x2016 boundary rather than emit garbage.
                _log?.Invoke($"[sector-udp] stream desync (size={size}); resyncing");
                _stream.Clear();
                _aligned = false;
                break;
            }
            if (_stream.Count < size) break;

            byte[] body = new byte[size - PacketHeader.WireSize];
            _stream.CopyTo(PacketHeader.WireSize, body, 0, body.Length);
            _stream.RemoveRange(0, size);
            (frames ??= new()).Add(Packet.ForOpcode(op, body));
        }
        return frames ?? None;
    }
}
