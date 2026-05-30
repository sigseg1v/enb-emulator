// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>enter &lt;name&gt;</c> -- run the GlobalTicketRequest -> MasterJoin
/// -> sector LOGIN -> drain-to-START handshake against an existing
/// avatar in the cached list. Prints sector + neighbour summary.
/// </summary>
public sealed class EnterCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public EnterCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "enter";
    public string Summary => "enter the world as the named character";
    public string Usage   => "enter <firstname>";

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Global is null || _ctx.AvatarList is null)
        {
            await output.WriteLineAsync("not logged in -- run `login` first").ConfigureAwait(false);
            return 1;
        }
        if (_ctx.Sector is not null)
        {
            await output.WriteLineAsync(
                $"already in sector {_ctx.ActiveSectorId} -- restart the REPL to switch")
                .ConfigureAwait(false);
            return 1;
        }
        if (args.Count < 1)
        {
            await output.WriteLineAsync("usage: enter <firstname>").ConfigureAwait(false);
            return 1;
        }

        string firstName = args[0];
        int slot = -1;
        int sectorId = -1;
        for (int i = 0; i < _ctx.AvatarList.Avatars.Length; i++)
        {
            var a = _ctx.AvatarList.Avatars[i];
            if (string.Equals(a.Data.FirstName, firstName, StringComparison.OrdinalIgnoreCase))
            {
                slot = i;
                sectorId = a.Info.SectorId > 0
                    ? a.Info.SectorId
                    : CharacterClass.StartSector(a.Data.Race, a.Data.Profession);
                break;
            }
        }
        if (slot < 0)
        {
            await output.WriteLineAsync(
                $"no character named '{firstName}' in the cached list (run `list`)")
                .ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync(
            $"entering sector {sectorId} on slot {slot} as {firstName}...")
            .ConfigureAwait(false);

        SectorEnterDriver.SectorEntryResult result;
        try
        {
            result = await SectorEnterDriver.EnterAsync(_ctx, _ctx.Global, slot, sectorId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"enter failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }

        _ctx.Sector = result.Sector;
        _ctx.GameId = result.GameId;
        _ctx.ActiveSlot = result.Slot;
        _ctx.ActiveSectorId = result.SectorId;

        await output.WriteLineAsync(
            $"in-sector: gameId=0x{result.GameId:X8} startId=0x{result.StartId:X8} " +
            $"handshake-frames={result.HandshakeFrames.Count}")
            .ConfigureAwait(false);

        // The 0x0005 START frame terminates the handshake but more
        // CREATE / positional / chat frames keep arriving for several
        // seconds after that as the sector finishes its initial fanout.
        // Drain whatever lands in the next 2 seconds so the neighbour
        // summary has a fuller picture without hanging the prompt.
        var extra = await DrainBriefly(result.Sector, TimeSpan.FromSeconds(2), ct)
            .ConfigureAwait(false);

        var allFrames = new List<Packet>(result.HandshakeFrames.Count + extra.Count);
        allFrames.AddRange(result.HandshakeFrames);
        allFrames.AddRange(extra);

        PrintNeighbourSummary(allFrames, selfGameId: result.GameId, output);
        return 0;
    }

    private static async Task<List<Packet>> DrainBriefly(
        EncryptedTcpConnection conn, TimeSpan window, CancellationToken outer)
    {
        var frames = new List<Packet>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(window);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var p = await conn.ReceiveAsync(cts.Token).ConfigureAwait(false);
                if (p is null) break;
                frames.Add(p);
            }
        }
        catch (OperationCanceledException)
        {
            // expected when the window expires
        }
        catch (Exception)
        {
            // best-effort drain; suppress so the neighbour summary still prints
        }
        return frames;
    }

    private static void PrintNeighbourSummary(
        IReadOnlyList<Packet> frames, int selfGameId, TextWriter output)
    {
        var objects = new Dictionary<int, ObjectInfo>();
        var positions = new Dictionary<int, (float X, float Y, float Z)>();
        var avatarNames = new Dictionary<int, string>();

        foreach (var p in frames)
        {
            switch (p.Header.Opcode)
            {
                case 0x0004: DecodeCreate(p, objects); break;
                case 0x0007: DecodeRemove(p, objects); break;
                case 0x0008: DecodeSimplePos(p, positions); break;
                case 0x0040: DecodeConstantPos(p, positions); break;
                case 0x0061: DecodeAvatarDescription(p, avatarNames); break;
            }
        }

        foreach (var (id, name) in avatarNames)
        {
            if (objects.TryGetValue(id, out var o))
                objects[id] = o with { Name = name };
        }

        (float X, float Y, float Z) self = positions.TryGetValue(selfGameId, out var p0)
            ? p0 : (0, 0, 0);

        var others = objects.Values
            .Where(o => o.GameId != selfGameId)
            .Select(o => (Info: o, Pos: positions.TryGetValue(o.GameId, out var p) ? p : (X: 0f, Y: 0f, Z: 0f)))
            .Select(t => (t.Info, t.Pos, Dist: Distance(self, t.Pos)))
            .OrderBy(t => t.Dist)
            .ToList();

        int avatarCount = others.Count(t => t.Info.IsAvatar);
        int otherCount = others.Count - avatarCount;

        output.WriteLine($"nearby: {others.Count} objects ({avatarCount} avatars, {otherCount} other)");

        if (others.Count == 0)
        {
            output.WriteLine("  (no other objects observed in the post-START drain window)");
            return;
        }

        int shown = 0;
        const int maxRows = 20;
        foreach (var (info, pos, dist) in others)
        {
            if (shown++ >= maxRows)
            {
                output.WriteLine($"  ... +{others.Count - maxRows} more");
                break;
            }
            string label = info.Name ?? $"<gid=0x{info.GameId:X8}>";
            string kind = info.IsAvatar ? "AVATAR " : "object ";
            string distStr = dist > 0 ? $"d={dist:0}" : "d=?";
            output.WriteLine(
                $"  {kind} type={info.Type,3} basset={info.BaseAsset,5}  {label,-22}  {distStr}");
        }
    }

    private static float Distance((float X, float Y, float Z) a, (float X, float Y, float Z) b)
    {
        if (a == (0f, 0f, 0f) || b == (0f, 0f, 0f)) return 0;
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private sealed record ObjectInfo(
        int GameId,
        sbyte Type,
        short BaseAsset,
        string? Name)
    {
        public bool IsAvatar => Type == 1;
    }

    private static void DecodeCreate(Packet p, Dictionary<int, ObjectInfo> dst)
    {
        var s = p.Payload.Span;
        if (s.Length < 11) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        short basset = BinaryPrimitives.ReadInt16LittleEndian(s.Slice(8, 2));
        sbyte type = (sbyte)s[10];
        dst[gameId] = new ObjectInfo(gameId, type, basset, Name: null);
    }

    private static void DecodeRemove(Packet p, Dictionary<int, ObjectInfo> dst)
    {
        var s = p.Payload.Span;
        if (s.Length < 4) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        dst.Remove(gameId);
    }

    private static void DecodeSimplePos(Packet p, Dictionary<int, (float, float, float)> dst)
    {
        var s = p.Payload.Span;
        if (s.Length < 20) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        float x = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(8, 4));
        float y = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(12, 4));
        float z = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(16, 4));
        dst[gameId] = (x, y, z);
    }

    private static void DecodeConstantPos(Packet p, Dictionary<int, (float, float, float)> dst)
    {
        var s = p.Payload.Span;
        if (s.Length < 16) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        float x = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(4, 4));
        float y = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(8, 4));
        float z = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(12, 4));
        dst[gameId] = (x, y, z);
    }

    private static void DecodeAvatarDescription(Packet p, Dictionary<int, string> dst)
    {
        var s = p.Payload.Span;
        // AvatarDescription: uint32 AvatarID, then AvatarData (first 20
        // bytes are first_name NUL-padded ASCII).
        if (s.Length < 4 + 20) return;
        int avatarId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        var nameSpan = s.Slice(4, 20);
        int nul = nameSpan.IndexOf((byte)0);
        if (nul < 0) nul = nameSpan.Length;
        string name = Encoding.ASCII.GetString(nameSpan[..nul]);
        if (name.Length > 0) dst[avatarId] = name;
    }
}
