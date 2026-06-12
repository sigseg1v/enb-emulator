// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace N7.CliClient.Net;

/// <summary>
/// A full-duplex MVAS/sector UDP client. Lets the headless CLI act as a real
/// client for the in-flight phase instead of a passive observer behind the
/// proxy.
/// </summary>
/// <remarks>
/// <para>
/// Mechanism: the server routes a player's downstream sector stream to whatever
/// addr/port last sent it MVAS (<c>SetPlayerPortIP</c>, server/src/UDP_MVAS.cpp),
/// sending it FROM the MVAS port (server/src/PlayerConnection.cpp via the
/// player's m_UDPConnection == the MVAS connection -- see proxy/UDPClient.h:36).
/// So once we send our own 0x1004 position from this socket, the server both
/// accepts our position AND streams the live sector fanout back to us here. In
/// the live stack the proxy normally fills that role (it scrapes the Win32
/// client's engine position); a headless client has no engine, so it feeds
/// position itself. This is NOT a server change -- it is exactly the protocol
/// the proxy speaks, from a different endpoint.
/// </para>
/// <para>
/// Downstream framing: each datagram is a 12-byte <c>EnbUdpHeader</c> whose
/// opcode is <c>0x2016 PACKET_SEQUENCE</c> (a batch of complete inner frames)
/// or <c>0x201A PACKET_C_SEQUENCE</c> (a continuation chunk of a split frame).
/// The payload is a stream of <c>EnbTcpHeader { short size; short opcode }</c>
/// frames (size = total frame length) -- the same framing the CLI already
/// decodes over the proxy TCP channel, but plaintext here (the proxy adds RC4
/// only for the client TCP). We treat 0x2016+0x201A payloads as one continuous
/// byte stream and emit each complete inner frame.
/// </para>
/// </remarks>
public sealed class SectorUdpClient : IDisposable
{
    private const int HeaderSize = 12;

    private readonly Socket _sock;
    private readonly IPEndPoint _server;
    private readonly Action<Packet> _onInbound;
    private readonly Action<string>? _log;
    private readonly SectorStreamReassembler _reasm;
    private int _seq;
    private int _rxDatagrams;
    private CancellationTokenSource? _cts;
    private Task? _recvTask;

    /// <summary>Server-suggested send rate (Hz) from 0x1007; default 20.</summary>
    public int Frequency => _reasm.Frequency;

    /// <summary>Local UDP port the socket bound to (server replies here).</summary>
    public int LocalPort => ((IPEndPoint)_sock.LocalEndPoint!).Port;

    public SectorUdpClient(string host, int port, Action<Packet> onInbound, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentNullException.ThrowIfNull(onInbound);
        _onInbound = onInbound;
        _log = log;
        _reasm = new SectorStreamReassembler(log);
        IPAddress ip = IPAddress.TryParse(host, out var parsed)
            ? parsed
            : Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
        _server = new IPEndPoint(ip, port);
        _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // Deliberately UNCONNECTED + bound: the server's reply can arrive from
        // a different source (NAT rewrites it on the way back through docker),
        // and a connect() would silently drop anything not from the exact peer
        // we sent to. The proxy hits the same trap -- see proxy/UDPClient.h:30.
        _sock.Bind(new IPEndPoint(IPAddress.Any, 0));
    }

    /// <summary>Begin the background receive loop.</summary>
    public void Start(CancellationToken outer)
    {
        if (_recvTask is { IsCompleted: false }) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var ct = _cts.Token;
        _recvTask = Task.Run(async () =>
        {
            var buf = new byte[65536];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var r = await _sock.ReceiveFromAsync(buf, SocketFlags.None, from, ct).ConfigureAwait(false);
                    if (r.ReceivedBytes > 0) ProcessDatagram(buf.AsSpan(0, r.ReceivedBytes));
                }
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex) { _log?.Invoke($"[sector-udp] {ex.GetType().Name}: {ex.Message}"); }
        }, ct);
    }

    /// <summary>Send one MVAS position (+heading) update for the player.</summary>
    public void SendPosition(int playerId, float x, float y, float z,
        (float X, float Y, float Z)? heading = null)
    {
        byte[] dg = MvasClient.BuildDatagram(
            playerId, Interlocked.Increment(ref _seq), x, y, z, heading);
        _sock.SendTo(dg, _server);
    }

    /// <summary>Datagrams received from the server so far (diagnostic).</summary>
    public int ReceivedDatagrams => _rxDatagrams;

    private void ProcessDatagram(ReadOnlySpan<byte> dg)
    {
        if (dg.Length < HeaderSize) return;
        _rxDatagrams++;
        if (_rxDatagrams <= 8)
        {
            ushort opcode = BinaryPrimitives.ReadUInt16LittleEndian(dg.Slice(2, 2));
            _log?.Invoke($"[sector-udp] rx #{_rxDatagrams} op=0x{opcode:X4} len={dg.Length}");
        }

        foreach (Packet frame in _reasm.Push(dg))
        {
            try { _onInbound(frame); }
            catch (Exception ex) { _log?.Invoke($"[sector-udp] ingest 0x{frame.Header.Opcode:X4}: {ex.Message}"); }
        }
    }

    public async Task StopAsync()
    {
        var cts = _cts; var task = _recvTask;
        _cts = null; _recvTask = null;
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
        if (task is not null) { try { await task.ConfigureAwait(false); } catch { } }
        cts.Dispose();
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _sock.Dispose();
        _cts?.Dispose();
    }
}
