// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using N7.CliClient.Net;
using N7.CliClient.Repl;
using N7.Tools.PcapInventory;

// ----------------------------------------------------------------------------
// pcap-inventory: decode a proxy<->server sector capture (.pcapng) into a
// human-readable inventory of every nav/station/gate, mob and resource the
// captured client came within range of -- with location, name, type, combat
// level (mobs), faction (mobs) and disposition (0x0089 RELATIONSHIP).
//
//   pcap-inventory <input.pcapng> [output.txt]
//
// This is a one-shot INVENTORY decoder, not a live-visibility model: it
// ignores 0x0007 REMOVE so flying out of range never deletes an object already
// catalogued. It reuses the real CliClient.Core decoders (SectorStreamReassembler,
// SectorWorld, AuxDataRecord) so the byte semantics stay in lock-step with the
// CLI client and its tests.
//
// Caveat surfaced in the report itself: navs have huge signature ranges so the
// nav set is reliable, but mobs/resources are range-gated -- the listing is
// "everything the capture's flight path passed", which is NOT provably the
// whole sector.
// ----------------------------------------------------------------------------

if (args.Length < 1 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("usage: pcap-inventory <input.pcapng> [output.txt]");
    Console.Error.WriteLine("  Decodes a proxy<->server sector UDP capture into a nav/mob/resource inventory.");
    Console.Error.WriteLine("  Default output: <input>.inventory.txt (next to the input).");
    Console.Error.WriteLine("  Tip: on Windows you can drag a .pcapng file onto pcap-inventory.exe.");
    return PauseOnOwnConsole(args.Length < 1 ? 2 : 0);
}

string inputPath = args[0];
if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"error: input not found: {inputPath}");
    return PauseOnOwnConsole(2);
}
string outputPath = args.Length >= 2
    ? args[1]
    : Path.ChangeExtension(inputPath, null) + ".inventory.txt";

// Compact 0x2019 RESOURCE_OBJECT_CREATE (proxy CreateResource): GameID i32@0,
// BaseAsset u16@4, Scale f32@6, HSV0@10, HSV1@14, Pos f32@18/22/26,
// Orient[4]@30-45, Name(u16 len)@46. SectorWorld does not model resources, so
// decode them here into a separate accumulating table.
var resources = new Dictionary<int, (int baseAsset, float x, float y, float z, string name)>();

// 0x0089 RELATIONSHIP side-table so resource rows can be annotated too (the
// SectorWorld also absorbs these for navs/mobs).
var relationships = new Dictionary<int, (int reaction, bool attacking)>();

var world = new SectorWorld();
var reassemblers = new Dictionary<string, SectorStreamReassembler>();

int datagrams = 0, frames = 0;
foreach (var dg in PcapNgReader.Read(inputPath))
{
    datagrams++;
    if (!reassemblers.TryGetValue(dg.FlowKey, out var ra))
        reassemblers[dg.FlowKey] = ra = new SectorStreamReassembler();

    foreach (var pkt in ra.Push(dg.Payload))
    {
        frames++;
        ushort op = pkt.Header.Opcode;
        var body = pkt.Payload.Span;

        if (op == 0x0007) continue; // ignore REMOVE: accumulate, do not evict

        if (op == 0x2019)
        {
            DecodeResource(body, resources);
            continue;
        }

        if (op == 0x0089 && body.Length >= 9)
        {
            int gid = BinaryPrimitives.ReadInt32BigEndian(body[..4]);
            relationships[gid] = (BinaryPrimitives.ReadInt32LittleEndian(body.Slice(4, 4)), body[8] != 0);
        }

        world.Ingest(Packet.ForOpcode(op, body.ToArray()));
    }
}

var report = BuildReport(inputPath, datagrams, frames, world, resources, relationships);
File.WriteAllText(outputPath, report);

Console.WriteLine($"decoded {frames} frames from {datagrams} UDP datagrams across {reassemblers.Count} flow(s)");
Console.WriteLine($"wrote {outputPath}");
return PauseOnOwnConsole(0);

// When this process OWNS its console (it was double-clicked or had a file
// dragged onto it from Explorer, so the window closes the instant we return),
// hold the window open so the user can read the result or the error. When we
// share a console with a parent shell (run from a terminal / `just`), return
// immediately -- a pause prompt there would be a nuisance. The discriminator is
// GetConsoleProcessList: a fresh own-console has exactly one attached process.
static int PauseOnOwnConsole(int exitCode)
{
    if (OwnsConsole())
    {
        Console.Error.WriteLine();
        Console.Error.Write("Press Enter to close...");
        Console.In.ReadLine();
    }
    return exitCode;
}

static bool OwnsConsole()
{
    if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected)
        return false;
    try
    {
        var buf = new uint[2];
        return NativeConsole.GetConsoleProcessList(buf, (uint)buf.Length) <= 1;
    }
    catch
    {
        return false; // no console attached (e.g. headless) -- never block
    }
}

// ----------------------------------------------------------------------------

static void DecodeResource(ReadOnlySpan<byte> body,
    Dictionary<int, (int, float, float, float, string)> resources)
{
    if (body.Length < 48) return;
    int gid = BinaryPrimitives.ReadInt32LittleEndian(body[..4]);
    int ba = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(4, 2));
    float x = BinaryPrimitives.ReadSingleLittleEndian(body.Slice(18, 4));
    float y = BinaryPrimitives.ReadSingleLittleEndian(body.Slice(22, 4));
    float z = BinaryPrimitives.ReadSingleLittleEndian(body.Slice(26, 4));
    string name = "";
    const int nameOff = 46;
    if (nameOff + 2 <= body.Length)
    {
        int nl = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(nameOff, 2));
        if (nameOff + 2 + nl <= body.Length)
            name = Encoding.ASCII.GetString(body.Slice(nameOff + 2, nl));
    }
    resources[gid] = (ba, x, y, z, name);
}

static string BuildReport(string inputPath, int datagrams, int frames,
    SectorWorld world,
    Dictionary<int, (int baseAsset, float x, float y, float z, string name)> resources,
    Dictionary<int, (int reaction, bool attacking)> relationships)
{
    string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
    string Loc(float x, float y, float z) => $"({F(x)}, {F(y)}, {F(z)})";

    var sb = new StringBuilder();
    sb.AppendLine($"# Sector inventory decoded from {Path.GetFileName(inputPath)}");
    sb.AppendLine($"# {frames} reassembled frames from {datagrams} UDP datagrams");
    sb.AppendLine("# Accumulating decode (0x0007 REMOVE ignored). Navs are reliable;");
    sb.AppendLine("# mobs/resources are range-gated -- only what the flight path passed.");
    sb.AppendLine();

    var tracked = world.NearestTo(0).Select(t => t.Obj).ToList();

    // ---- NAVS / STATIONS / GATES (CreateType 10/11 gate, 12 station, 3 planet, 37 nav/deco) ----
    var navlike = tracked
        .Where(o => o.CreateType is 3 or 10 or 11 or 12 or 37 || o.IsNav)
        .OrderBy(o => o.Name ?? "", StringComparer.Ordinal).ToList();
    sb.AppendLine($"== NAVS / STATIONS / GATES ({navlike.Count}) ==");
    foreach (var o in navlike)
    {
        string kind = SectorWorld.TypeName(o);
        string pos = o.HasPos ? Loc(o.X, o.Y, o.Z) : "(no pos)";
        string vis = o.IsNav ? (o.Visited == true ? " visited" : " unvisited") : "";
        string sig = o.Signature is { } s ? $" sig={F(s)}" : "";
        string react = o.Reaction is { } ? $" [{SectorWorld.ReactionName(o.Reaction)}]" : "";
        sb.AppendLine($"  [{kind,-12}] {o.Name ?? "<unnamed>",-32} gid=0x{o.GameId:X8} base={o.BaseAsset} {pos}{sig}{vis}{react}");
    }

    // Player ships live in the high game-id range (0x40000000+); they get the
    // same CREATE opcode as mobs but are NOT enemies. Route them out.
    static bool IsPlayerGid(int gid) => (uint)gid >= 0x40000000u;

    // ---- MOBS & NPCS (CreateType 0 spawn, 1 mob, 42 turret) -- disposition shown ----
    var mobs = tracked.Where(o => o.CreateType is 0 or 1 or 42 && !IsPlayerGid(o.GameId) && !o.IsAvatar)
        .OrderBy(o => o.Name ?? "", StringComparer.Ordinal).ToList();
    sb.AppendLine();
    sb.AppendLine($"== MOBS & NPCS ({mobs.Count}) -- disposition is this client's relationship ==");
    foreach (var o in mobs)
    {
        string pos = o.HasPos ? Loc(o.X, o.Y, o.Z) : "(no pos)";
        string lvl = o.Level is { } l ? l.ToString() : "?";
        string disp = SectorWorld.ReactionName(o.Reaction);
        string atk = o.IsAttacking == true ? " ATTACKING" : "";
        string fac = string.IsNullOrEmpty(o.Faction) ? "" : $" faction={o.Faction}";
        sb.AppendLine($"  {o.Name ?? "<unnamed>",-28} lvl={lvl,-4} {disp,-9}{atk} gid=0x{o.GameId:X8} mobTemplate(base)={o.BaseAsset}{fac} {pos}");
    }

    // ---- PLAYERS / AVATARS (high gid range or saw 0x0061) ----
    var players = tracked.Where(o => (IsPlayerGid(o.GameId) || o.IsAvatar) && !o.IsNav
            && o.CreateType is not (3 or 10 or 11 or 12 or 37))
        .OrderBy(o => o.Name ?? "", StringComparer.Ordinal).ToList();
    if (players.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine($"== PLAYERS / AVATARS ({players.Count}) ==");
        foreach (var o in players)
        {
            string pos = o.HasPos ? Loc(o.X, o.Y, o.Z) : "(no pos)";
            string fac = string.IsNullOrEmpty(o.Faction) ? "" : $" faction={o.Faction}";
            sb.AppendLine($"  {o.Name ?? "<unnamed>",-28} gid=0x{o.GameId:X8}{fac} {pos}");
        }
    }

    // ---- RESOURCES (0x2019) ----
    sb.AppendLine();
    sb.AppendLine($"== RESOURCES ({resources.Count}) ==");
    foreach (var kv in resources.OrderBy(k => k.Value.name, StringComparer.Ordinal))
    {
        var (ba, x, y, z, name) = kv.Value;
        string react = relationships.TryGetValue(kv.Key, out var r)
            ? $" [{SectorWorld.ReactionName(r.reaction)}]" : "";
        sb.AppendLine($"  {name,-28} gid=0x{kv.Key:X8} resTemplate(base)={ba} {Loc(x, y, z)}{react}");
    }

    // ---- anything tracked but uncategorised (diagnostic) ----
    var other = tracked
        .Where(o => !(o.CreateType is 0 or 1 or 3 or 10 or 11 or 12 or 37 or 42) && !o.IsNav && !o.IsAvatar)
        .ToList();
    if (other.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine($"== OTHER tracked objects ({other.Count}) [diagnostic] ==");
        foreach (var o in other)
        {
            string pos = o.HasPos ? Loc(o.X, o.Y, o.Z) : "(no pos)";
            sb.AppendLine($"  ct={o.CreateType?.ToString() ?? "?"} {o.Name ?? "<unnamed>",-26} gid=0x{o.GameId:X8} base={o.BaseAsset} {pos}");
        }
    }

    return sb.ToString();
}
