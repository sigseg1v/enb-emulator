// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0092 CAMERA_CONTROL. Wire (struct CameraControl, 8 bytes):
///   int32 Message; int32 GameID.  Message is emitted FIRST.
/// BOTH fields are BIG-endian on the wire: Player::SendCameraControl memcpy's
/// the struct verbatim, and every caller pre-swaps -- the GameID is always
/// passed as ntohl(GameID) (SectorManager.cpp, PlayerConnection.cpp, the proxy
/// UDPProxyToClient.cpp) and Message is a pre-swapped literal (0x05000000 ->
/// reads 5) or ntohl(atoi(param)). So a host-LE read of GameID yields the
/// byte-swapped garbage id (the classic ntohl trap), and the camera would point
/// at no object. Capture cross-check: object 0x000001C2 (==450) appears here
/// (Packet #1712) and as the same id in SetTarget (#1368) and VerbUpdate (#1372).
/// Source: struct CameraControl (PacketStructures.h), Player::SendCameraControl
/// (PlayerConnection.cpp:4516) and its callers.
/// </summary>
public sealed class CameraControlRecord : PacketRecord
{
    public CameraControlRecord(ReadOnlySpan<byte> payload) : base(0x0092, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"CAMERA_CONTROL truncated -- {Payload.Length} bytes, expected 8"); return; }
        FHex(sb, 0, "Message", ReadI32BE(Payload, 0), "(BE -- pre-swapped at emit)");
        FHex(sb, 4, "GameID",  ReadI32BE(Payload, 4), "(BE -- ntohl at emit)");
    }
}
