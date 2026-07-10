#!/usr/bin/env python3
"""Build the galaxy gate-routing map (data/galaxy.json) the sector survey driver
uses to know where every gate leads.

Two inputs:
  1. The live content DB (net7) -- every stargate object and its display name,
     plus the sector-id -> sector-name catalog. Queried read-only via
     `docker compose exec postgres psql`. This is the authoritative, regenerable
     source for gate names and adjacency.
  2. data/systems.json -- system -> [sector ids] membership. EnB groups sectors
     into star SYSTEMS, and an inter-system gate is labelled with the destination
     SYSTEM name ("Gate to Beta Hydri System"), not a sector, so resolving those
     needs a membership table. That table is NOT in our DB; it was derived from
     the external reconstruct dataset's public galaxy maps (the enbmaps sector
     menu, which encodes system->sector membership) and is committed as a static
     topology reference. Regenerate it only when the galaxy layout itself changes.

Output data/galaxy.json carries:
  gate_dest            norm(gatename) -> dest sid           (sector-named gates, direction-agnostic)
  gate_dest_by_sector  {sid: {norm(gatename): dest sid}}    (per-source; covers paired-name wormholes)
  wormhole_pairs       [[sidA, sidB], ...]                  (named two-endpoint wormholes)
  system_gate          norm(gatename) -> dest system name   (inter-system gates)
  system_entry         "SysA>SysB" -> landing sid in SysB    (reciprocal-gate resolution)
  adjacency            {sid: [neighbour sids]}              (full undirected sector graph)
  sector_system        {sid: system name}
  systems              system -> [sids]
  sid2name             {sid: sector name}

Run:  python3 build_galaxy.py         (writes ../data/galaxy.json)
"""
import os, re, json, html, subprocess, collections

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "..", "data")
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))


def norm(s):
    s = html.unescape(str(s)).lower().replace("\xa0", " ")
    s = s.replace("´", "'").replace("`", "'").replace("’", "'")
    return re.sub(r"[^a-z0-9]+", "", s)


def psql(sql):
    """Read-only query against the content DB, tab-separated rows."""
    out = subprocess.run(
        ["docker", "compose", "exec", "-T", "postgres",
         "psql", "-U", "net7", "-d", "net7", "-tA", "-F", "\t", "-c", sql],
        cwd=REPO, capture_output=True, text=True, check=True).stdout
    return [ln.split("\t") for ln in out.splitlines() if ln.strip()]


# ---- inputs -------------------------------------------------------------
sid2name, norm2sid = {}, {}
for sid, name in psql("SELECT sector_id, name FROM sectors ORDER BY sector_id;"):
    sid = int(sid)
    sid2name[sid] = name.strip()
    norm2sid[norm(name)] = sid

gates = [(int(sec), gname) for sec, _sname, gname in psql(
    "SELECT so.sector_id, s.name, so.name "
    "FROM sector_objects_stargates g "
    "JOIN sector_objects so ON so.sector_object_id = g.stargate_id "
    "JOIN sectors s ON s.sector_id = so.sector_id "
    "ORDER BY so.sector_id;")]

systems = json.load(open(os.path.join(DATA, "systems.json")))
sid2sys = {x: sysn for sysn, sids in systems.items() for x in sids}
SYSNORM = {norm(s): s for s in systems}


# ---- resolvers ----------------------------------------------------------
def resolve_sector(phrase):
    """Fuzzy sector-name -> sid: exact, then strip a leading 'the', then take
    the first known sector token when the phrase joins two ('Freya and Akerons
    Gate')."""
    n = norm(phrase)
    if n in norm2sid:
        return norm2sid[n]
    n2 = re.sub(r"^the", "", n)
    if n2 in norm2sid:
        return norm2sid[n2]
    for part in re.split(r"\band\b", phrase):
        pn = norm(part)
        if pn in norm2sid:
            return norm2sid[pn]
    return None


def dest_phrase(name):
    low = name.lower()
    i = low.rfind(" to ")
    return name[i + 4:].strip() if i >= 0 else None


# in-sector devices / dead / instanced gates that are never a real crossing
DEAD = re.compile(
    r"disabled|non-functional|testing|tada-o|dev2|undocumented|anomaly|one\?\?|"
    r"upper (atmosphere|orbit)|atmosphere|orbit|moon risco|risco|"
    r"under construction|staging point|launchpad|rift to the unknown|"
    r"to the unknown|replica|escape velocity", re.I)
INTERNAL = re.compile(
    r"accel|atmosphere|upper orbit|jumpgate|launchpad|testing|dev2|disabled|"
    r"non-functional|tada-o|one\?\?|replica|staging", re.I)


# ---- classify gates -----------------------------------------------------
gate_dest = {}                                        # norm(gatename) -> dest sid
gate_dest_by_sector = collections.defaultdict(dict)   # sid -> {norm(gatename): dest sid}
system_gate = {}                                      # norm(gatename) -> dest system
sec_edges = collections.defaultdict(set)              # sid -> {dest sids}
sys_gate_rows = []                                    # (src sid, dst system, gatename)
skipped = []
for sec, gname in gates:
    dp = dest_phrase(gname)
    sysmatch = None
    if dp:
        cand = re.sub(r"\s+system$", "", dp, flags=re.I).strip()
        if norm(cand) in SYSNORM:
            sysmatch = SYSNORM[norm(cand)]
    if sysmatch:
        system_gate[norm(gname)] = sysmatch
        sys_gate_rows.append((sec, sysmatch, gname))
        continue
    if dp and not DEAD.search(gname):
        dsid = resolve_sector(dp)
        if dsid is not None:
            gate_dest[norm(gname)] = dsid
            gate_dest_by_sector[sec][norm(gname)] = dsid
            sec_edges[sec].add(dsid)
            sec_edges[dsid].add(sec)
            continue
    skipped.append((sec, gname))

# paired-name wormholes: a named gate (no 'to X') whose EXACT name appears in
# exactly two sectors is a two-endpoint wormhole (e.g. "Vishao's Gate" links
# Antares Frontier <-> Vishao's Cove). Each endpoint resolves the shared name to
# the OTHER sector, so this is per-source.
byname = collections.defaultdict(set)
for sec, gname in skipped:
    if INTERNAL.search(gname):
        continue
    byname[norm(gname)].add(sec)
wormhole_pairs = []
for gk, secs in byname.items():
    if len(secs) == 2:
        a, b = sorted(secs)
        gate_dest_by_sector[a][gk] = b
        gate_dest_by_sector[b][gk] = a
        sec_edges[a].add(b)
        sec_edges[b].add(a)
        wormhole_pairs.append([a, b])


# ---- reciprocal system-entry landings -----------------------------------
# Crossing SysA -> SysB you land in the SysB member whose gate points back to
# SysA. Collect, for each sector, the systems it has an outbound gate to (via a
# system gate, or via a sector gate whose destination sits in another system).
sec_out_systems = collections.defaultdict(set)
for sec, sysn, _ in sys_gate_rows:
    sec_out_systems[sec].add(sysn)
for sec, dests in sec_edges.items():
    for d in dests:
        if d in sid2sys:
            sec_out_systems[sec].add(sid2sys[d])

system_entry = {}
for SA in systems:
    for SB in systems:
        if SA == SB:
            continue
        cand = [m for m in systems[SB] if SA in sec_out_systems.get(m, ())]
        if len(cand) == 1:
            system_entry[f"{SA}>{SB}"] = cand[0]
        elif len(cand) > 1:
            system_entry[f"{SA}>{SB}"] = sorted(cand)   # ambiguous: keep list

# adjacency = sector edges + uniquely-resolved system-gate landings
adj = collections.defaultdict(set)
for k, v in sec_edges.items():
    adj[k] |= set(v)
for sec, SB, _ in sys_gate_rows:
    SA = sid2sys.get(sec)
    land = system_entry.get(f"{SA}>{SB}") if SA else None
    if isinstance(land, int):
        adj[sec].add(land)
        adj[land].add(sec)


# ---- emit ---------------------------------------------------------------
galaxy = dict(
    systems=systems,
    sector_system={str(k): v for k, v in sid2sys.items()},
    gate_dest=gate_dest,
    gate_dest_by_sector={str(k): v for k, v in gate_dest_by_sector.items()},
    wormhole_pairs=wormhole_pairs,
    system_gate=system_gate,
    system_entry=system_entry,
    adjacency={str(k): sorted(v) for k, v in adj.items()},
    sid2name={str(k): v for k, v in sid2name.items()},
)
outp = os.path.join(DATA, "galaxy.json")
with open(outp, "w") as f:
    json.dump(galaxy, f, indent=0, sort_keys=True)
    f.write("\n")

print(f"wrote {outp}")
print(f"  sectors={len(sid2name)} gates={len(gates)} "
      f"gate_dest(sector)={len(gate_dest)} system_gate={len(system_gate)} "
      f"wormhole_pairs={len(wormhole_pairs)} skipped={len(skipped)}")
res = sum(1 for v in system_entry.values() if isinstance(v, int))
amb = sum(1 for v in system_entry.values() if isinstance(v, list))
print(f"  system_entry resolved={res} ambiguous={amb}")


def nm(s):
    return sid2name.get(s, f"?{s}")


for label, key, expect in [
        ("Sol>Aragoth", "Sol>Aragoth", "Freya"),
        ("Sol>Beta Hydri", "Sol>Beta Hydri", "Glenn")]:
    got = system_entry.get(key)
    got = nm(got) if isinstance(got, int) else got
    flag = "OK" if got == expect else f"EXPECTED {expect}"
    print(f"  check {label} landing = {got}  [{flag}]")
print(f"  Antares Frontier (1505) adjacency = {[nm(x) for x in sorted(adj.get(1505, []))]}")
