// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes.Records;

namespace N7.CliClient.Repl;

/// <summary>
/// A running model of what the server has told us is in the current
/// sector, accumulated from the inbound frame stream. Fed by
/// <see cref="SessionContext"/>'s packet hook; read by <c>enter</c>'s
/// arrival summary and the in-sector <c>list</c> command.
/// </summary>
/// <remarks>
/// Decodes the frames whose layout is pinned and stable:
/// 0x0004 CREATE, 0x0007 REMOVE, 0x0008/0x0040/0x003E positional updates,
/// 0x0061 AVATAR_DESCRIPTION (names + avatar flag), 0x2018
/// STATIC_OBJECT_CREATE (nav/station/gate names + signature), and 0x0099
/// NAVIGATION (nav-list membership + visited flag). Level is not present in
/// any of these frames -- it arrives via RPGInfo aux -- so it is reported
/// as unknown rather than guessed. Thread-safe: the background sector
/// drain ingests while the REPL thread renders.
/// </remarks>
/// <summary>What happened to a tracked object as a frame was ingested.</summary>
public enum WorldEventKind
{
    /// <summary>An entity first became identifiable (got a name) -- it
    /// "appeared in scanner range".</summary>
    Appeared,
    /// <summary>A previously-announced entity was removed (0x0007).</summary>
    Departed,
}

/// <summary>
/// A semantic change to the sector model, surfaced by <see cref="SectorWorld.Ingest"/>
/// so the REPL can narrate it. Carries only the kind + game id; the narrator
/// reads the current model (name/level/reaction/distance) at format time, so a
/// late-arriving aux/relationship frame is reflected. <see cref="DepartedKind"/>
/// and <see cref="DepartedName"/> snapshot the entity at REMOVE time because the
/// model entry is gone by the time the narrator runs.
/// </summary>
public readonly record struct WorldEvent(
    WorldEventKind Kind,
    int GameId,
    string? DepartedKind = null,
    string? DepartedName = null);

public sealed class SectorWorld
{
    public sealed class Tracked
    {
        /// <summary>True once an Appeared event has been emitted for this
        /// entity, so it is announced exactly once even as later frames
        /// (position/aux/relationship) keep updating it.</summary>
        public bool Announced;
        public int GameId;
        public int? CreateType;     // server create-type byte (0x0004 / 0x2018)
        public short? BaseAsset;
        public string? Name;
        public bool IsAvatar;       // saw a 0x0061 AVATAR_DESCRIPTION
        public bool IsNav;          // 0x2018 IS_NAV/NavType>0, or a 0x0099 entry
        public int? NavType;        // 1 = minor nav, 2 = major nav
        public bool? OnRadar;       // 0x2018 sig_flags HAS_NAV (on the minimap)
        public float? Signature;
        public bool? Visited;
        public bool HasPos;
        public float X, Y, Z;
        public int? Level;          // CombatLevel from a 0x001B aux, if announced
        public float? MaxSpeed;     // ship MaxSpeed from a 0x001B ship aux, if announced
        public string? Faction;     // FactionIdentifier from a 0x001B mob aux, if announced
        public int? Reaction;       // 0x0089 RELATIONSHIP reaction (0=attack,1=shun,2=friendly,3=adoration)
        public bool? IsAttacking;   // 0x0089 RELATIONSHIP is-attacking flag
    }

    private readonly object _gate = new();
    private readonly Dictionary<int, Tracked> _objects = new();

    /// <summary>Drop all tracked state (called on entering a new sector).</summary>
    public void Reset()
    {
        lock (_gate) _objects.Clear();
    }

    /// <summary>Number of tracked objects.</summary>
    public int Count { get { lock (_gate) return _objects.Count; } }

    private Tracked GetOrAdd(int gameId)
    {
        if (!_objects.TryGetValue(gameId, out var t))
        {
            t = new Tracked { GameId = gameId };
            _objects[gameId] = t;
        }
        return t;
    }

    private static readonly IReadOnlyList<WorldEvent> NoEvents = Array.Empty<WorldEvent>();

    /// <summary>
    /// Update the model from one inbound frame and return any semantic events
    /// (entity appeared / departed) the frame produced, for the REPL to
    /// narrate. Returns an empty list for frames that change nothing
    /// announceable. Never throws -- a malformed frame must not kill the drain.
    /// </summary>
    public IReadOnlyList<WorldEvent> Ingest(Packet p)
    {
        try
        {
            var s = p.Payload.Span;
            List<WorldEvent>? events = null;
            lock (_gate)
            {
                switch (p.Header.Opcode)
                {
                    case 0x0004: IngestCreate(s); break;
                    case 0x2018: IngestStatic(s, ref events); break;
                    case 0x2019: IngestResource(s); break;
                    case 0x0007: IngestRemove(s, ref events); break;
                    case 0x0008: IngestSimplePos(s); break;
                    case 0x0040: IngestConstantPos(s); break;
                    case 0x003E: IngestAdvancedPos(s); break;
                    case 0x003F: IngestPlanetPos(s); break;
                    case 0x0061: IngestAvatar(s, ref events); break;
                    case 0x0099: IngestNavigation(s); break;
                    case 0x001B: IngestAux(s, ref events); break;
                    case 0x0089: IngestRelationship(s); break;
                }
            }
            return (IReadOnlyList<WorldEvent>?)events ?? NoEvents;
        }
        catch
        {
            // Best-effort: a malformed frame must not kill the drain.
            return NoEvents;
        }
    }

    /// <summary>
    /// Emit an Appeared event the first time an entity (player avatar or mob)
    /// becomes identifiable -- i.e. it has a name. Static scenery (navs,
    /// stations, planets, deco, resources) is not a "scanner contact" and is
    /// never announced. Latches via <see cref="Tracked.Announced"/> so a given
    /// entity announces exactly once even as later frames keep updating it.
    /// Caller holds <c>_gate</c>.
    /// </summary>
    private static void MaybeAnnounce(Tracked t, ref List<WorldEvent>? events)
    {
        if (t.Announced) return;
        if (string.IsNullOrEmpty(t.Name)) return;
        bool isEntity = t.IsAvatar || t.CreateType is 0 or 1;
        if (!isEntity) return;
        t.Announced = true;
        (events ??= new()).Add(new WorldEvent(WorldEventKind.Appeared, t.GameId));
    }

    private void IngestCreate(ReadOnlySpan<byte> s)
    {
        if (s.Length < 11) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        var t = GetOrAdd(gameId);
        t.BaseAsset = BinaryPrimitives.ReadInt16LittleEndian(s.Slice(8, 2));
        t.CreateType = (sbyte)s[10];
    }

    private void IngestStatic(ReadOnlySpan<byte> s, ref List<WorldEvent>? events)
    {
        // 0x2018 StaticMap::FormStaticPacket layout: GameID@0, CreateType u8@4,
        // BaseAsset i16@5, Scale@7, HSV@11/15/19, relationship@23, posType@24,
        // PosX@25, PosY@29, PosZ@33, orient[4]@37, Signature@53, sig_flags@57,
        // then AddDataLS Name@58 (u16 len + chars, no NUL).
        if (s.Length < 58) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        var t = GetOrAdd(gameId);
        t.CreateType = s[4];
        t.BaseAsset = BinaryPrimitives.ReadInt16LittleEndian(s.Slice(5, 2));
        t.X = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(25, 4));
        t.Y = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(29, 4));
        t.Z = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(33, 4));
        t.HasPos = true;
        t.Signature = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(53, 4));
        // sig_flags@57 (server StaticMap::FormStaticPacket): 0x80 HAS_VISITED,
        // 0x40 IS_HUGE, 0x20 IS_NAV, 0x10 HAS_NAV (on-minimap), low nibble =
        // NavType (0/1/2). This is the ONLY reliable nav/deco signal in the
        // create path. A HIDDEN nav (clickable but off-minimap: NavType>0,
        // AppearsInRadar=0 -- e.g. "Traders Run") gets NO 0x0099 NAVIGATION
        // frame, because the server gates SendNavigation on AppearsInRadar
        // (NavTypeClass.cpp:417). So keying nav-ness off 0x0099 alone mislabels
        // every hidden nav as a deco; reading the flag byte fixes that.
        byte sigFlags = s[57];
        int navTypeFromStatic = sigFlags & 0x0F;
        if ((sigFlags & 0x20) != 0 || navTypeFromStatic > 0)
        {
            t.IsNav = true;
            if (navTypeFromStatic > 0) t.NavType = navTypeFromStatic;
            t.OnRadar = (sigFlags & 0x10) != 0;
            t.Visited = (sigFlags & 0x80) != 0;
        }
        if (s.Length >= 60)
        {
            ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(58, 2));
            if (60 + nameLen <= s.Length)
                t.Name = Encoding.ASCII.GetString(s.Slice(60, nameLen));
        }
        // 0x2018 is static content (navs/stations/scenery); MaybeAnnounce
        // gates on entity-ness so these never produce a scanner-contact line.
        MaybeAnnounce(t, ref events);
    }

    private void IngestResource(ReadOnlySpan<byte> s)
    {
        // 0x2019 RESOURCE_OBJECT_CREATE (Resource::FormStaticPacket,
        // server/src/ResourceClass.cpp:694) -- the compact prospectable-resource
        // create. The proxy normally CONSUMES this and re-emits a 0x0004 CREATE
        // Type=38 to the client; on the raw server leg (a direct MVAS-takeover
        // downstream, which bypasses the proxy) the CLI sees the 0x2019 itself,
        // so it must locate roids from this. Layout pinned in
        // ResourceObjectCreateRecord: GameID@0, BaseAsset i16@4, Position@18,
        // NameLen i16@46, Name@48. CreateType=38 makes TypeName() report
        // "resource" (the same kind the proxy-expanded 0x0004 Type=38 yields).
        if (s.Length < 48) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        var t = GetOrAdd(gameId);
        t.CreateType = 38;
        t.BaseAsset = BinaryPrimitives.ReadInt16LittleEndian(s.Slice(4, 2));
        t.X = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(18, 4));
        t.Y = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(22, 4));
        t.Z = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(26, 4));
        t.HasPos = true;
        short nameLen = BinaryPrimitives.ReadInt16LittleEndian(s.Slice(46, 2));
        if (nameLen > 0 && 48 + nameLen <= s.Length)
            t.Name = Encoding.Latin1.GetString(s.Slice(48, nameLen));
    }

    private void IngestRemove(ReadOnlySpan<byte> s, ref List<WorldEvent>? events)
    {
        if (s.Length < 4) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        if (_objects.TryGetValue(gameId, out var t))
        {
            if (t.Announced)
                (events ??= new()).Add(new WorldEvent(
                    WorldEventKind.Departed, gameId, TypeName(t), t.Name));
            _objects.Remove(gameId);
        }
    }

    private void IngestSimplePos(ReadOnlySpan<byte> s)
    {
        if (s.Length < 20) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        SetPos(gameId,
            BinaryPrimitives.ReadSingleLittleEndian(s.Slice(8, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(s.Slice(12, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(s.Slice(16, 4)));
    }

    private void IngestConstantPos(ReadOnlySpan<byte> s)
    {
        if (s.Length < 16) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        SetPos(gameId,
            BinaryPrimitives.ReadSingleLittleEndian(s.Slice(4, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(s.Slice(8, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(s.Slice(12, 4)));
    }

    private void IngestAdvancedPos(ReadOnlySpan<byte> s)
    {
        // 0x003E: int16 bitmask, then gameId@2, timestamp@6, pos@10..21.
        if (s.Length < 42) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(2, 4));
        SetPos(gameId,
            BinaryPrimitives.ReadSingleLittleEndian(s.Slice(10, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(s.Slice(14, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(s.Slice(18, 4)));
    }

    private void IngestPlanetPos(ReadOnlySpan<byte> s)
    {
        // 0x003F PLANET_POSITIONAL_UPDATE: GameID@0, TimeStamp@4, Position@8
        // (3 floats), then orbit/rotate fields. Planets/moons announce their
        // location here rather than via 0x0040/0x0008, so without this they
        // show up with an unknown distance.
        if (s.Length < 20) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        SetPos(gameId,
            BinaryPrimitives.ReadSingleLittleEndian(s.Slice(8, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(s.Slice(12, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(s.Slice(16, 4)));
    }

    private void IngestAux(ReadOnlySpan<byte> s, ref List<WorldEvent>? events)
    {
        // 0x001B AUX_DATA carries an object's name (and, for ships/mobs, its
        // combat level) in a flag-driven variable-layout body. Reuse the
        // catalog's schema walker rather than hand-rolling offsets. The self
        // PlayerIndex keys to GameID 0 and its nested names belong to no one
        // sector object -- skip it.
        var sum = AuxDataRecord.TryExtractSummary(s);
        if (sum is not { } a || a.GameId == 0) return;
        var t = GetOrAdd((int)a.GameId);
        if (!string.IsNullOrEmpty(a.Name)) t.Name = a.Name;
        if (a.CombatLevel is { } lvl) t.Level = (int)lvl;
        if (a.MaxSpeed is { } spd && spd > 0) t.MaxSpeed = spd;
        if (!string.IsNullOrEmpty(a.Faction)) t.Faction = a.Faction;
        // Aux is usually where a mob first gets its name -- the announce point.
        MaybeAnnounce(t, ref events);
    }

    private void IngestRelationship(ReadOnlySpan<byte> s)
    {
        // 0x0089 RELATIONSHIP (struct Relationship, 9 bytes): ObjectID i32 BE
        // (server applies ntohl at emit), Reaction i32 LE, IsAttacking u8.
        // Reaction: 0=ATTACK, 1=SHUN, 2=FRIENDLY, 3=ADORATION.
        if (s.Length < 9) return;
        int gameId = BinaryPrimitives.ReadInt32BigEndian(s[..4]);
        var t = GetOrAdd(gameId);
        t.Reaction = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(4, 4));
        t.IsAttacking = s[8] != 0;
    }

    private void IngestAvatar(ReadOnlySpan<byte> s, ref List<WorldEvent>? events)
    {
        if (s.Length < 24) return;
        int avatarId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        var t = GetOrAdd(avatarId);
        t.IsAvatar = true;
        var nameSpan = s.Slice(4, 20);
        int nul = nameSpan.IndexOf((byte)0);
        if (nul < 0) nul = nameSpan.Length;
        string name = Encoding.ASCII.GetString(nameSpan[..nul]);
        if (name.Length > 0) t.Name = name;
        // A player avatar announces here (0x0061 carries the name + avatar flag).
        MaybeAnnounce(t, ref events);
    }

    private void IngestNavigation(ReadOnlySpan<byte> s)
    {
        if (s.Length < 14) return;
        int gameId = BinaryPrimitives.ReadInt32LittleEndian(s[..4]);
        var t = GetOrAdd(gameId);
        t.IsNav = true;
        t.Signature = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(4, 4));
        t.Visited = s[8] != 0;
        t.NavType = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(9, 4));
    }

    private void SetPos(int gameId, float x, float y, float z)
    {
        var t = GetOrAdd(gameId);
        t.X = x; t.Y = y; t.Z = z; t.HasPos = true;
    }

    /// <summary>
    /// Human-readable disposition for a 0x0089 RELATIONSHIP reaction value
    /// (server enum: 0=ATTACK, 1=SHUN, 2=FRIENDLY, 3=ADORATION).
    /// </summary>
    public static string ReactionName(int? reaction) => reaction switch
    {
        0 => "hostile",
        1 => "shun",
        2 => "friendly",
        3 => "adoration",
        null => "unknown",
        _ => $"reaction={reaction}",
    };

    /// <summary>Human-readable object kind for a tracked entry.</summary>
    public static string TypeName(Tracked t)
    {
        if (t.IsAvatar) return "avatar";
        int? ct = t.CreateType;
        string baseName = ct switch
        {
            0  => "mob spawn",
            1  => "mob",
            3  => "planet",
            10 => "stargate",
            11 => "stargate",
            12 => "station",
            37 => t.IsNav ? NavLabel(t) : "deco",
            38 => "resource",
            40 => "radiation",
            41 => "gravity well",
            42 => "turret",
            _  => t.IsNav ? NavLabel(t) : "object",
        };
        return baseName;
    }

    /// <summary>
    /// Label a nav by its class. A HIDDEN nav (clickable but off-minimap:
    /// 0x2018 IS_NAV/NavType>0 with HAS_NAV clear) emits no 0x0099 and is what
    /// the create path would otherwise mislabel as a deco. NavType 2 is a
    /// destination-only ("major") nav; 1 is a warp-path nav.
    /// </summary>
    private static string NavLabel(Tracked t)
    {
        bool hidden = t.OnRadar == false;
        bool major  = t.NavType == 2;
        return (hidden, major) switch
        {
            (true,  true)  => "nav (hidden, major)",
            (true,  false) => "nav (hidden)",
            (false, true)  => "nav (major)",
            _              => "nav",
        };
    }

    /// <summary>
    /// One navigable destination in the current sector -- a discovered/visible
    /// nav, stargate, or station the player can warp/gate/dock to. The set
    /// <c>navs</c> lists and that <c>warp</c>/<c>gate</c>/<c>dock</c> complete
    /// against.
    /// </summary>
    public readonly record struct NavTarget(
        int GameId, string Name, string Kind,
        bool IsGate, bool IsStation, bool IsNav,
        bool? Visited, bool? OnRadar, float? Dist);

    // Stargate create-types are 10/11; station is 12 (see TypeName).
    private static bool IsNavigable(Tracked t)
        => t.IsNav || t.CreateType is 10 or 11 or 12;

    /// <summary>
    /// The navigable destinations in the sector (navs, stargates, stations),
    /// nearest first. Only named entries are returned -- an unnamed target
    /// cannot be picked by name and is not a useful warp/gate destination. The
    /// self avatar is excluded. Used by <c>navs</c> and by the warp/gate/dock
    /// completion candidate sets.
    /// </summary>
    public IReadOnlyList<NavTarget> NavTargets(int selfGameId)
    {
        lock (_gate)
        {
            (float X, float Y, float Z)? self =
                _objects.TryGetValue(selfGameId, out var me) && me.HasPos
                    ? (me.X, me.Y, me.Z)
                    : null;

            return _objects.Values
                .Where(o => o.GameId != selfGameId && IsNavigable(o) && !string.IsNullOrEmpty(o.Name))
                .Select(o =>
                {
                    bool isGate = o.CreateType is 10 or 11;
                    bool isStation = o.CreateType is 12;
                    float? d = (self is { } sp && o.HasPos)
                        ? MathF.Sqrt(
                            (o.X - sp.X) * (o.X - sp.X) +
                            (o.Y - sp.Y) * (o.Y - sp.Y) +
                            (o.Z - sp.Z) * (o.Z - sp.Z))
                        : (float?)null;
                    string kind = isGate ? "stargate" : isStation ? "station" : TypeName(o);
                    return new NavTarget(
                        o.GameId, o.Name!, kind, isGate, isStation,
                        !isGate && !isStation, o.Visited, o.OnRadar, d);
                })
                .OrderBy(t => t.Dist ?? float.MaxValue)
                .ToArray();
        }
    }

    /// <summary>
    /// Names of every navigable destination (navs/gates/stations), nearest
    /// first, de-duplicated -- the candidate set for <c>warp</c> and
    /// <c>gate</c> Tab completion.
    /// </summary>
    public IReadOnlyList<string> NavTargetNames(int selfGameId)
        => NavTargets(selfGameId)
            .Select(t => t.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Names of station destinations only -- the candidate set for
    /// <c>dock</c> completion.</summary>
    public IReadOnlyList<string> StationNames(int selfGameId)
        => NavTargets(selfGameId)
            .Where(t => t.IsStation)
            .Select(t => t.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Snapshot the tracked objects, sorted by distance from the self
    /// avatar (objects with no known position sort last).
    /// </summary>
    public IReadOnlyList<(Tracked Obj, float? Dist)> NearestTo(int selfGameId)
    {
        lock (_gate)
        {
            (float X, float Y, float Z)? self =
                _objects.TryGetValue(selfGameId, out var me) && me.HasPos
                    ? (me.X, me.Y, me.Z)
                    : null;

            return _objects.Values
                .Where(o => o.GameId != selfGameId)
                .Select(o =>
                {
                    float? d = (self is { } sp && o.HasPos)
                        ? MathF.Sqrt(
                            (o.X - sp.X) * (o.X - sp.X) +
                            (o.Y - sp.Y) * (o.Y - sp.Y) +
                            (o.Z - sp.Z) * (o.Z - sp.Z))
                        : (float?)null;
                    return (Obj: o, Dist: d);
                })
                .OrderBy(t => t.Dist ?? float.MaxValue)
                .ToArray();
        }
    }

    /// <summary>Own position if known, else null.</summary>
    public (float X, float Y, float Z)? SelfPosition(int selfGameId)
    {
        lock (_gate)
            return _objects.TryGetValue(selfGameId, out var me) && me.HasPos
                ? (me.X, me.Y, me.Z) : null;
    }

    /// <summary>
    /// Snapshot of the player's own ship/avatar, accumulated from the same
    /// frame stream (0x0061 name, 0x001B ship aux level, 0x003E position).
    /// </summary>
    public (string? Name, int? Level, (float X, float Y, float Z)? Pos) SelfSnapshot(int selfGameId)
    {
        lock (_gate)
        {
            if (!_objects.TryGetValue(selfGameId, out var me))
                return (null, null, null);
            (float, float, float)? pos = me.HasPos ? (me.X, me.Y, me.Z) : null;
            return (me.Name, me.Level, pos);
        }
    }

    /// <summary>Own ship MaxSpeed (units/sec) if a ship aux announced it.</summary>
    public float? SelfSpeed(int selfGameId)
    {
        lock (_gate)
            return _objects.TryGetValue(selfGameId, out var me) ? me.MaxSpeed : null;
    }

    /// <summary>A tracked object's position by game id, or null if unknown/no pos.</summary>
    public (float X, float Y, float Z)? PositionOf(int gameId)
    {
        lock (_gate)
            return _objects.TryGetValue(gameId, out var t) && t.HasPos
                ? (t.X, t.Y, t.Z) : null;
    }

    /// <summary>
    /// Snapshot the fields the narrator needs to describe an entity: its kind
    /// label, name, combat level, hostility (relationship reaction), and
    /// distance from self. Pulled at narration time so late-arriving aux /
    /// relationship frames are reflected even though the Appeared event fired
    /// earlier. Returns null if the game id is no longer tracked.
    /// </summary>
    public (string Kind, string? Name, int? Level, int? Reaction, float? Dist)? Describe(int gameId, int selfGameId)
    {
        lock (_gate)
        {
            if (!_objects.TryGetValue(gameId, out var t)) return null;
            float? dist = null;
            if (_objects.TryGetValue(selfGameId, out var me) && me.HasPos && t.HasPos)
                dist = MathF.Sqrt(
                    (t.X - me.X) * (t.X - me.X) +
                    (t.Y - me.Y) * (t.Y - me.Y) +
                    (t.Z - me.Z) * (t.Z - me.Z));
            return (TypeName(t), t.Name, t.Level, t.Reaction, dist);
        }
    }

    /// <summary>A tracked object's display name by game id, or null.</summary>
    public string? NameOf(int gameId)
    {
        lock (_gate)
            return _objects.TryGetValue(gameId, out var t) ? t.Name : null;
    }

    /// <summary>
    /// Resolve a player/object name to its game id, case-insensitively. Prefers
    /// avatars (a 0x0061 description was seen) over other named objects, so
    /// `group invite Yee` lands on the player and not a same-named nav. Returns
    /// null when no tracked object carries that name.
    /// </summary>
    public int? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        lock (_gate)
        {
            return _objects.Values
                .Where(o => o.Name is { } n && string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.IsAvatar)
                .Select(o => (int?)o.GameId)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Print the nearby summary: own position to 4 d.p. then one row per
    /// object (kind, name, level, distance), nearest first.
    /// </summary>
    public void Render(TextWriter output, int selfGameId, int maxRows = 30)
    {
        var (selfName, selfLevel, self) = SelfSnapshot(selfGameId);
        string here = self is { } sp
            ? $"({F4(sp.X)}, {F4(sp.Y)}, {F4(sp.Z)})"
            : "(unknown -- no positional update for self yet)";
        string selfLbl = selfName is { Length: > 0 } ? selfName : "<self>";
        string selfLvl = selfLevel is { } sl ? sl.ToString(CultureInfo.InvariantCulture) : "-";
        output.WriteLine(
            AnsiPalette.Muted("you: ") + AnsiPalette.Accent(selfLbl) +
            AnsiPalette.Muted($" (lvl {selfLvl})  gameId=0x{selfGameId:X8}  pos=") +
            AnsiPalette.Value(here));

        var rows = NearestTo(selfGameId);
        int navs = rows.Count(r => r.Obj.IsNav);
        int avatars = rows.Count(r => r.Obj.IsAvatar);
        output.WriteLine(
            AnsiPalette.Head($"nearby: {rows.Count} objects") +
            AnsiPalette.Muted($" ({avatars} avatars, {navs} navs)  [lvl '-' = not yet announced via aux]"));

        if (rows.Count == 0)
        {
            output.WriteLine(AnsiPalette.Muted(
                "  (nothing tracked yet -- fly around or wait for the sector fanout)"));
            return;
        }

        int shown = 0;
        foreach (var (o, dist) in rows)
        {
            if (shown++ >= maxRows)
            {
                output.WriteLine(AnsiPalette.Muted($"  ... +{rows.Count - maxRows} more"));
                break;
            }
            string kind = TypeName(o);
            string name = o.Name ?? $"<gid=0x{o.GameId:X8}>";
            string lvl = o.Level is { } lv ? lv.ToString(CultureInfo.InvariantCulture) : "-";
            string distStr = dist is { } d ? $"d={d:0.0}" : "d=?";
            string visited = o.IsNav ? (o.Visited == true ? " visited" : " unvisited") : "";
            // Pad each field BEFORE colouring so the escape bytes don't count
            // toward the column widths.
            output.WriteLine(
                "  " + AnsiPalette.Info($"{kind,-12}") + " " +
                AnsiPalette.Muted("lvl ") + AnsiPalette.Value($"{lvl,-3}") + " " +
                AnsiPalette.Accent($"{name,-28}") + " " +
                AnsiPalette.Muted($"{distStr,-12}") + AnsiPalette.Muted(visited));
        }
    }

    private static string F4(float v) => v.ToString("0.0000", CultureInfo.InvariantCulture);
}
