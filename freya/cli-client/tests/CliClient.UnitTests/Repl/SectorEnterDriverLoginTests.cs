// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

/// <summary>
/// Pins the FromSectorID byte in the sector LOGIN MasterJoin. The sector server
/// reads this off the join (HandleSectorLogin -> m_FromSectorID) and uses it in
/// Player::SetStartingPosition: FindGate(m_FromSectorID) finds the gate in the
/// destination sector that links back to the origin, and the avatar is placed
/// there. A re-join that sends 0 (the fresh-login value) makes the server treat
/// it as a plain login -> LoadPosition() restores the stale saved x,y,z from the
/// previous sector, dumping the avatar far from the destination gate. So the
/// handoff re-join MUST carry the real origin sector here.
/// </summary>
public sealed class SectorEnterDriverLoginTests
{
    // MasterJoin field layout (BE int32s): FromSectorID is the 7th field, at
    // byte offset 24 of the 64-byte MasterJoin that leads the LOGIN payload.
    private const int FromSectorIdOffset = 24;
    private const int ToSectorIdOffset = 20;

    [Fact]
    public void BuildLoginPacket_FreshLogin_FromSectorIdIsZero()
    {
        // Default (no handoff): 0 tells the server "plain login, LoadPosition()".
        var pkt = SectorEnterDriver.BuildLoginPacket("user-deadbeef", 0x40000010, 1015);
        int from = BinaryPrimitives.ReadInt32BigEndian(pkt.Payload.Span.Slice(FromSectorIdOffset, 4));
        Assert.Equal(0, from);
    }

    [Fact]
    public void BuildLoginPacket_Handoff_EncodesOriginSectorBigEndian()
    {
        // Gated from Alpha Centauri (e.g. sector 1020) into Luna (1015): the
        // re-join must echo the origin so the server spawns us at Luna's
        // "Gate to Alpha Centauri", not at the stale saved position.
        const int luna = 1015;
        const int alphaCentauri = 1020;

        var pkt = SectorEnterDriver.BuildLoginPacket("user-deadbeef", 0x40000010, luna, alphaCentauri);
        var span = pkt.Payload.Span;

        Assert.Equal(luna,          BinaryPrimitives.ReadInt32BigEndian(span.Slice(ToSectorIdOffset, 4)));
        Assert.Equal(alphaCentauri, BinaryPrimitives.ReadInt32BigEndian(span.Slice(FromSectorIdOffset, 4)));
    }
}
