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
    Console.Error.WriteLine("usage: pcap-inventory <input.pcapng> [output.txt] [--json [out.json]]");
    Console.Error.WriteLine("  Decodes a proxy<->server sector UDP capture into a nav/mob/resource inventory.");
    Console.Error.WriteLine("  Default output: <input>.inventory.txt (next to the input).");
    Console.Error.WriteLine("  --json emits a machine-readable per-object dump (sector-tagged) for the");
    Console.Error.WriteLine("         import pipeline instead of the human-readable text report.");
    Console.Error.WriteLine("  Tip: on Windows you can drag a .pcapng file onto pcap-inventory.exe.");
    return PauseOnOwnConsole(args.Length < 1 ? 2 : 0);
}

// --json [path] toggles the machine-readable dump used by the sector-import
// pipeline. Strip it (and its optional path arg) out of the positional args.
bool jsonMode = false;
string? jsonOut = null;
{
    var rest = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--json")
        {
            jsonMode = true;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                jsonOut = args[++i];
        }
        else rest.Add(args[i]);
    }
    args = rest.ToArray();
}

string inputPath = args[0];
if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"error: input not found: {inputPath}");
    return PauseOnOwnConsole(2);
}
string outputPath = jsonMode
    ? (jsonOut ?? Path.ChangeExtension(inputPath, null) + ".objects.json")
    : args.Length >= 2
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

// Sector-boundary tracking. A capture starts in the PREVIOUS sector and gates
// into the target, so objects created before the gate belong to the wrong
// sector and must not be imported. We tag every object with the sector that was
// "current" when its CREATE arrived: currentSector advances on each sector-id
// carrier seen in stream order (0x0036 SERVER_REDIRECT, 0x003A SERVER_HANDOFF,
// 0x006F GLOBAL_TICKET). gidSector pins each gid to the sector it first
// appeared under, so flying back across a gate cannot re-tag it.
int? currentSector = null;
var gidSector = new Dictionary<int, int>();
var gidFirstFrame = new Dictionary<int, int>();
var markers = new List<(int frame, string op, int sector)>();
void TagCreate(int gid, int frame)
{
    if (!gidFirstFrame.ContainsKey(gid)) gidFirstFrame[gid] = frame;
    if (currentSector is { } sec && !gidSector.ContainsKey(gid))
        gidSector[gid] = sec;
}

var readDatagrams = PcapReader.IsClassicPcap(inputPath)
    ? PcapReader.Read(inputPath)
    : PcapNgReader.Read(inputPath);

int datagrams = 0, frames = 0;
foreach (var dg in readDatagrams)
{
    datagrams++;
    if (!reassemblers.TryGetValue(dg.FlowKey, out var ra))
        reassemblers[dg.FlowKey] = ra = new SectorStreamReassembler();

    foreach (var pkt in ra.Push(dg.Payload))
    {
        frames++;
        ushort op = pkt.Header.Opcode;
        var body = pkt.Payload.Span;

        // --- sector-id carriers: advance currentSector in stream order ---
        if (op == 0x0036 && body.Length >= 4)
        {
            currentSector = BinaryPrimitives.ReadInt32LittleEndian(body[..4]);
            markers.Add((frames, "0x0036", currentSector.Value));
        }
        else if (op == 0x003A && body.Length >= 24)
        {
            currentSector = BinaryPrimitives.ReadInt32BigEndian(body.Slice(20, 4));
            markers.Add((frames, "0x003A", currentSector.Value));
        }
        else if (op == 0x006F && body.Length >= 28)
        {
            currentSector = BinaryPrimitives.ReadInt32BigEndian(body.Slice(24, 4));
            markers.Add((frames, "0x006F", currentSector.Value));
        }

        if (op == 0x0007) continue; // ignore REMOVE: accumulate, do not evict

        if (op == 0x2019)
        {
            int rgid = BinaryPrimitives.ReadInt32LittleEndian(body[..4]);
            TagCreate(rgid, frames);
            DecodeResource(body, resources);
            continue;
        }

        if (op == 0x0089 && body.Length >= 9)
        {
            int gid = BinaryPrimitives.ReadInt32BigEndian(body[..4]);
            relationships[gid] = (BinaryPrimitives.ReadInt32LittleEndian(body.Slice(4, 4)), body[8] != 0);
        }

        // CREATE-class opcodes carry the gid LE@0; tag it with the current sector.
        if (op is 0x0004 or 0x2018 && body.Length >= 4)
            TagCreate(BinaryPrimitives.ReadInt32LittleEndian(body[..4]), frames);

        world.Ingest(Packet.ForOpcode(op, body.ToArray()));
    }
}

if (jsonMode)
{
    string json = BuildJson(inputPath, datagrams, frames, world, resources,
        relationships, gidSector, gidFirstFrame, markers);
    File.WriteAllText(outputPath, json);
}
else
{
    string report = BuildReport(inputPath, datagrams, frames, world, resources, relationships);
    File.WriteAllText(outputPath, report);
}

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

// Machine-readable dump for the sector-import pipeline. Emits every decoded
// object with its sector tag (the sector that was current when its CREATE
// arrived) plus the ordered list of sector-id markers, so the Python side can
// (a) keep only objects belonging to the target sector and (b) audit the
// boundary. All policy (classify/exclude players+loot, dedupe across captures,
// 5k-replace, SQL gen) lives in Python; this is a pure decode-to-JSON.
static string BuildJson(string inputPath, int datagrams, int frames,
    SectorWorld world,
    Dictionary<int, (int baseAsset, float x, float y, float z, string name)> resources,
    Dictionary<int, (int reaction, bool attacking)> relationships,
    Dictionary<int, int> gidSector,
    Dictionary<int, int> gidFirstFrame,
    List<(int frame, string op, int sector)> markers)
{
    int? Sec(int gid) => gidSector.TryGetValue(gid, out var s) ? s : null;
    int? Frame(int gid) => gidFirstFrame.TryGetValue(gid, out var f) ? f : null;

    var objs = new List<object>();
    foreach (var t in world.NearestTo(0).Select(t => t.Obj))
    {
        relationships.TryGetValue(t.GameId, out var rel);
        objs.Add(new
        {
            gid = t.GameId,
            sector = Sec(t.GameId),
            frame = Frame(t.GameId),
            createType = t.CreateType,
            name = t.Name,
            baseAsset = t.BaseAsset,
            hasPos = t.HasPos,
            x = t.HasPos ? t.X : (float?)null,
            y = t.HasPos ? t.Y : (float?)null,
            z = t.HasPos ? t.Z : (float?)null,
            level = t.Level,
            faction = t.Faction,
            reaction = t.Reaction,
            isAttacking = t.IsAttacking,
            isAvatar = t.IsAvatar,
            isNav = t.IsNav,
            navType = t.NavType,
            onRadar = t.OnRadar,
            visited = t.Visited,
            signature = t.Signature,
            kind = SectorWorld.TypeName(t),
        });
    }
    foreach (var kv in resources)
    {
        var (ba, x, y, z, name) = kv.Value;
        relationships.TryGetValue(kv.Key, out var rel);
        objs.Add(new
        {
            gid = kv.Key,
            sector = Sec(kv.Key),
            frame = Frame(kv.Key),
            createType = (int?)38,
            name = name.Length == 0 ? null : name,
            baseAsset = (short?)(short)ba,
            hasPos = true,
            x = (float?)x,
            y = (float?)y,
            z = (float?)z,
            level = (int?)null,
            faction = (string?)null,
            reaction = relationships.TryGetValue(kv.Key, out var r) ? r.reaction : (int?)null,
            isAttacking = (bool?)null,
            isAvatar = false,
            isNav = false,
            navType = (int?)null,
            onRadar = (bool?)null,
            visited = (bool?)null,
            signature = (float?)null,
            kind = "resource",
        });
    }

    var doc = new
    {
        capture = Path.GetFileName(inputPath),
        datagrams,
        frames,
        markers = markers.Select(m => new { m.frame, op = m.op, m.sector }).ToList(),
        objects = objs,
    };
    return System.Text.Json.JsonSerializer.Serialize(doc,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
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
