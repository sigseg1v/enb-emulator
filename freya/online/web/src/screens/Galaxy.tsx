// SPDX-License-Identifier: MIT
// Freya Online -- Galaxy map (first tab).
//
// A stylized chart of the core sectors of the galaxy, drawn at FIXED, hand-
// placed coordinates (the content DB has no real galaxy_x/galaxy_y, so the
// positions here are the authored cartography from the design -- systems are
// nebula territories, sectors are nodes, gates are jump lanes). Each sector
// lights up with live "sonar" waves and a pilot count when players are in it.
//
// Two data sources, very different cadences:
//   - TOPOLOGY (/api/galaxy) -- fetched once. We use it ONLY to bridge the
//     authored node names to the content DB's sector ids, so the live counts
//     (which are keyed by sector id) can be matched back to a node on the map.
//   - OCCUPANCY (/api/galaxy/occupancy) -- polled. counts[sectorId] -> players.
//
// The name<->id bridge is gnorm(): a tolerant normalization (lowercased, glyph-
// and-punctuation-stripped, "asteroid belt"->"astrobelt", "(old)"/"maelstrom"
// folded) applied to BOTH the authored node name and the DB sector name, so
// "Blackbeard's Wake" / "Blackbeards Wake" and "AstroBelt Alpha" / "Asteroid
// Belt Alpha" resolve to the same key. A node whose name has no DB match simply
// never lights up (it is one of the minor sub-locations that rarely holds a
// pilot); the header total still reflects ALL online players from occ.total.
//
// Positions are authored and MUST NOT be changed.

import { useEffect, useMemo, useRef, useState } from 'react';
import type { CSSProperties } from 'react';
import type { GalaxyMap, GalaxyOccupancy } from '../types';
import * as api from '../api';

type Vars = CSSProperties & Record<string, string | number>;

const OCCUPANCY_POLL_MS = 15_000;
const W = 1960;
const H = 1280;

// ---- factions: OKLCH lightness / chroma / hue per territory ----
const F: Record<string, { label: string; L: number; C: number; H: number }> = {
  jenquai: { label: 'Jenquai', L: 0.62, C: 0.135, H: 256 },
  neutral: { label: 'Neutral', L: 0.70, C: 0.018, H: 250 },
  contested: { label: 'Contested', L: 0.60, C: 0.180, H: 305 },
  progen: { label: 'Progen', L: 0.58, C: 0.195, H: 26 },
  terran: { label: 'Terran', L: 0.64, C: 0.150, H: 150 },
  pirate: { label: 'Pirate', L: 0.74, C: 0.140, H: 72 },
};
const col = (f: string, L: number, a?: number) =>
  `oklch(${L} ${F[f].C} ${F[f].H}${a == null ? '' : ' / ' + a})`;
const node = (f: string) => col(f, 0.78); // bright node fill
const glowc = (f: string) => col(f, 0.70, 0.55);

// ---- systems: nebula territories (centre + radius are authored cartography) ----
interface Sys { name: string; f: string; cx: number; cy: number; r: number; }
const SYSTEMS: Sys[] = [
  { name: 'Capella', f: 'jenquai', cx: 413, cy: 205, r: 154 },
  { name: 'Antares', f: 'jenquai', cx: 193, cy: 288, r: 66 },
  { name: 'Sirius', f: 'jenquai', cx: 386, cy: 536, r: 132 },
  { name: 'Castor', f: 'jenquai', cx: 606, cy: 398, r: 110 },
  { name: 'Mazzaroth Maelstrom', f: 'neutral', cx: 165, cy: 481, r: 66 },
  { name: 'Unknown', f: 'neutral', cx: 82, cy: 646, r: 66 },
  { name: 'Mondara Maelstrom', f: 'neutral', cx: 1103, cy: 150, r: 66 },
  { name: 'Sol', f: 'neutral', cx: 993, cy: 508, r: 287 },
  { name: 'Beta Hydri', f: 'terran', cx: 248, cy: 867, r: 199 },
  { name: 'Tau Ceti', f: 'terran', cx: 662, cy: 757, r: 154 },
  { name: 'Alpha Centauri', f: 'terran', cx: 1075, cy: 922, r: 132 },
  { name: 'Aquitaine', f: 'terran', cx: 1296, cy: 1088, r: 66 },
  { name: 'Proxima Centauri', f: 'terran', cx: 1324, cy: 812, r: 110 },
  { name: '61 Cygni', f: 'contested', cx: 772, cy: 1005, r: 110 },
  { name: 'Smugglers Run', f: 'pirate', cx: 524, cy: 1143, r: 110 },
  { name: 'Vega', f: 'progen', cx: 1351, cy: 177, r: 132 },
  { name: 'Gallina', f: 'progen', cx: 1737, cy: 260, r: 154 },
  { name: 'Altair', f: 'progen', cx: 1434, cy: 508, r: 132 },
  { name: 'Deneb', f: 'progen', cx: 1655, cy: 619, r: 66 },
  { name: 'Aragoth', f: 'progen', cx: 1682, cy: 977, r: 265 },
];

// ---- sectors: [name, x, y] -- authored chart coordinates. Apostrophes are
// written as a plain ' (display + matching both want a real apostrophe). ----
const RAW: [string, number, number][] = [
  ['Antares Frontier', 187, 282], ['Dahin', 414, 178], ['Dahin Planet', 435, 115], ['Kailaasa', 496, 199],
  ["Kitara's Veil", 336, 147], ["Vishao's Cove", 336, 251], ['Yokan', 435, 283], ['Ishuan', 632, 360],
  ['Menorb', 569, 424], ['Swooping Eagle', 313, 530], ['Swooping Eagle Planet', 385, 464], ['Xipe Totec', 413, 588],
  ['Mazzaroth (old)', 159, 475], ['FishBowl', 76, 640],
  ['Carpenter', 356, 795], ['Cooper', 127, 927], ['Glenn', 242, 729], ["Glory's Orbit", 242, 861],
  ['Grissom', 242, 993], ['Grissom Planet', 242, 927], ['Shepard', 356, 927], ['Slayton', 174, 805],
  ['AstroBelt Alpha', 916, 587], ['AstroBelt Beta', 964, 464], ['AstroBelt Gamma', 930, 382], ['Earth', 960, 654],
  ['Equatorial Earth', 1038, 694], ['Europa', 766, 502], ['Ganymede', 787, 409], ['High Earth', 860, 683],
  ['Io', 844, 333], ['Jupiter', 842, 450], ['Luna', 948, 720], ['Mars', 1027, 353], ['Mars Alpha', 1006, 283],
  ['Mars Beta', 1097, 311], ['Mars Gamma', 1156, 360], ['Mercury', 1163, 502], ['Pluto & Charon', 1127, 567],
  ['Saturn', 878, 522], ['Uranus', 1025, 556], ['Venus', 1089, 417], ['Ceres / Thule', 1196, 435], ['Neptune', 1053, 497],
  ['Akerons Gate', 1100, 638], ['Mondara (old)', 1097, 144],
  ['Arduinne', 707, 793], ['Arduinne Planet', 713, 718], ['Inverness', 611, 827], ['Inverness Planet', 567, 751],
  ['New Edinburgh', 618, 671],
  ['Aganju', 724, 1014], ['Moto', 807, 984], ['der Todesengel (old)', 1058, 981], ['Witberg', 1134, 927],
  ['Zweihander', 1080, 851], ['Zweihander Planet', 1007, 894],
  ["Blackbeard's Wake", 479, 1159], ['Paramis', 556, 1115],
  ['Altair III', 1428, 436], ['Nostrand Vor', 1472, 511], ['Nostrand Vor Planet', 1385, 553], ['Adriel Prime', 1362, 814],
  ['Margesi', 1273, 798], ['Ardus (old)', 1290, 1082], ['Roc', 1649, 613],
  ['Endriago', 1644, 239], ['Endriago Planet', 1742, 189], ['Lagarto', 1731, 342], ['Lagarto Moon Risco', 1796, 265],
  ['Primus', 1407, 193], ['Primus Planet', 1337, 127], ['Tarsis', 1294, 214],
  ['Aragoth Prime', 1695, 1080], ['Fenris', 1524, 998], ['Freya', 1577, 799], ['Jotunheim', 1722, 800],
  ['Muspelheim', 1775, 1144], ['Nifleheim Cloud', 1523, 883], ['Odin Rex', 1803, 883], ["Odin's Belt", 1875, 971],
  ['Ragnarok', 1639, 868], ['Valkyrie Twins', 1676, 971], ["Varen's Girdle", 1577, 1090],
];

// Sectors that anchor their territory get a bold display label + pulse ring.
const HUBS = new Set([
  'Dahin', 'Antares Frontier', 'Swooping Eagle', 'Ishuan', 'Earth', 'Mars', 'Glenn',
  'New Edinburgh', 'Witberg', 'Aganju', "Blackbeard's Wake", 'Adriel Prime', 'Primus', 'Endriago',
  'Nostrand Vor', 'Roc', 'Aragoth Prime', 'Freya',
]);
const MINOR = /Planet|Moon|Belt|Cloud|Girdle|Twins|\(old\)/i;

function deriveType(n: string): string {
  if (/Planet/i.test(n)) return 'Planet';
  if (/Moon/i.test(n)) return 'Moon';
  if (/Belt/i.test(n)) return 'Asteroid Belt';
  if (/Cloud/i.test(n)) return 'Nebula';
  if (/Gate/i.test(n)) return 'Jump Gate';
  if (/Twins/i.test(n)) return 'Binary System';
  if (/Girdle/i.test(n)) return 'Ring System';
  if (/\(old\)/i.test(n)) return 'Deep Space';
  return 'Star System';
}

// Assign each sector to the territory whose footprint best contains it.
function systemOf(x: number, y: number): Sys {
  let best = SYSTEMS[0], bestScore = Infinity;
  for (const sm of SYSTEMS) {
    const score = Math.hypot(x - sm.cx, y - sm.cy) / sm.r;
    if (score < bestScore) { bestScore = score; best = sm; }
  }
  return best;
}

// Tolerant name key shared by the authored map and the DB sector names. Strips
// apostrophe glyphs, "(old)", folds "&"->"and", drops "maelstrom", maps
// "asteroid belt"->"astrobelt", then keeps only [a-z0-9]. Matches the backend's
// sector names (see store_galaxy.go) closely enough for every populated sector.
function gnorm(s: string): string {
  return s.toLowerCase()
    .replace(/[´`']/g, '')
    .replace(/\(old\)/g, '')
    .replace(/&/g, 'and')
    .replace(/\bmaelstrom\b/g, '')
    .replace(/asteroid belt/g, 'astrobelt')
    .replace(/[^a-z0-9]/g, '');
}

// ---- jump lanes: the real gate network. Intra-system = solid, inter-system =
// dashed "gate". Pulled from the gate list (docs/sector-gates.md). ----
const GATE_TXT = `
Adriel Prime|Freya
Adriel Prime|Margesi
Aganju|Inverness
Aganju|Moto
Akerons Gate|Freya
Akerons Gate|Pluto and Charon
Akerons Gate|Saturn
Altair III|Endriago
Altair III|Mars Gamma
Altair III|Moto
Altair III|Nostrand Vor
Antares Frontier|Vishao's Cove
Aragoth Prime|Muspelheim
Aragoth Prime|Valkyrie Twins
Aragoth Prime|Varens Girdle
Arduinne|Inverness
Ardus|Witberg
Asteroid Belt Alpha|Asteroid Belt Beta
Asteroid Belt Alpha|Earth
Asteroid Belt Alpha|Saturn
Asteroid Belt Beta|Asteroid Belt Gamma
Asteroid Belt Beta|Saturn
Asteroid Belt Beta|Venus
Asteroid Belt Gamma|Mars
Asteroid Belt Gamma|Saturn
Blackbeards Wake|Grissom
Blackbeards Wake|Inverness
Blackbeards Wake|Muspelheim
Blackbeards Wake|Paramis
Blackbeards Wake|Xipe Totec
Carpenter|Glenn
Carpenter|New Edinburgh
Carpenter|Shepard
Carpenter|Slayton
Ceres/Thule|Venus
Cooper|Grissom
Dahin|Kailaasa
Dahin|Kitara's Veil
Earth|Asteroid Belt Alpha
Earth|Equatorial Earth
Earth|High Earth
Earth|Luna
Endriago|Lagarto
Endriago|Primus
Equatorial Earth|Margesi
Europa|Jupiter
Europa|Swooping Eagle
Fenris|Valkyrie Twins
Fenris|Varens Girdle
Freya|Jotunheim
Freya|Nifleheim Cloud
Freya|Ragnarok
Freya|Witberg
Ganymede|Ishuan
Ganymede|Jupiter
Glenn|Saturn
Glenn|Slayton
Glenn|Swooping Eagle
Glorys Orbit|Slayton
Grissom|Shepard
High Earth|New Edinburgh
Inverness|New Edinburgh
Io|Jupiter
Io|Kailaasa
Ishuan|Yokan
Jotunheim|Odin Rex
Jotunheim|Ragnarok
Jupiter|Saturn
Kailaasa|Yokan
Kitara's Veil|Vishao's Cove
Lagarto|Mars Beta
Lagarto|Odins Belt
Lagarto|Roc
Margesi|Equatorial Earth
Mars|Mars Alpha
Mars|Mars Beta
Mars|Mars Gamma
Mars Alpha|Tarsis
Mars Beta|Lagarto
Mazzaroth Maelstrom|Swooping Eagle
Menorb|Ishuan
Mercury|Pluto and Charon
Mercury|Venus
Mondara Maelstrom|Tarsis
Muspelheim|Odins Belt
Neptune|Uranus
Nostrand Vor|Altair III
Odin Rex|Odins Belt
Odin Rex|Paramis
Paramis|Odin Rex
Pluto and Charon|Uranus
Primus|Tarsis
Ragnarok|Jotunheim
Roc|Lagarto
Saturn|Uranus
Tarsis|Primus
Venus|Mercury
Vishao's Cove|Kitara's Veil
Witberg|Zweihander
Witberg|der Todesengel
Xipe Totec|Swooping Eagle
Yokan|Swooping Eagle
Zweihander|Luna
`;

// ---- deterministic PRNG (no Math.random: identical layout every render) ----
function mulberry(seed: number): () => number {
  return function () {
    let t = (seed += 0x6d2b79f5);
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// ---- node model (computed once: authored data, no live state) ----
interface Node {
  n: string; x: number; y: number; f: string; sys: string;
  hub: boolean; minor: boolean; special: boolean; type: string; R: number;
}
const NODES: Node[] = RAW.map(([n, x, y]) => {
  const sm = systemOf(x, y);
  const hub = HUBS.has(n), minor = MINOR.test(n), special = n === 'Freya';
  return {
    n, x, y, f: sm.f, sys: sm.name, hub, minor, special,
    type: deriveType(n), R: hub ? 12 : minor ? 3.6 : 6.6,
  };
});
const byName: Record<string, Node> = Object.fromEntries(NODES.map(s => [s.n, s]));

// ---- lanes: dedup undirected, precompute the curved path once ----
interface Lane { a: string; b: string; gate: boolean; d: string; }
const LANES: Lane[] = (() => {
  const normMap: Record<string, Node> = {};
  NODES.forEach(s => { normMap[gnorm(s.n)] = s; });
  const out: Lane[] = [];
  const seen = new Set<string>();
  GATE_TXT.trim().split('\n').forEach(line => {
    const [a, b] = line.split('|');
    const sa = normMap[gnorm(a)], sb = normMap[gnorm(b)];
    if (!sa || !sb || sa === sb) return;
    const key = [sa.n, sb.n].sort().join('¦');
    if (seen.has(key)) return;
    seen.add(key);
    const gate = sa.sys !== sb.sys;
    const mx = (sa.x + sb.x) / 2, my = (sa.y + sb.y) / 2;
    const dx = sb.x - sa.x, dy = sb.y - sa.y, len = Math.hypot(dx, dy) || 1;
    const off = gate ? 22 : 10;
    const cx = mx + (-dy / len) * off, cy = my + (dx / len) * off;
    out.push({ a: sa.n, b: sb.n, gate, d: `M${sa.x} ${sa.y} Q${cx} ${cy} ${sb.x} ${sb.y}` });
  });
  return out;
})();

// ---- starfield + nebula jitter (one shared deterministic rng, consumed in a
// fixed order so the backdrop is byte-stable across renders) ----
interface Star { x: number; y: number; r: number; fill: string; opacity: number; twinkle: boolean; dur: number; }
interface Dust { x: number; y: number; r: number; opacity: number; }
interface Neb { sm: Sys; nd: number; dx: number; dy: number; rot: number; ry: number; dust: Dust[]; fs: number; ly: number; labelOpacity: number; }
const { STARS, NEBULAE } = (() => {
  const rnd = mulberry(20260611);
  const stars: Star[] = [];
  for (let i = 0; i < 440; i++) {
    const x = rnd() * W, y = rnd() * H, r = rnd() * 1.0 + 0.3;
    const bright = rnd() > 0.86;
    const opacity = rnd() * 0.5 + 0.1;
    const dur = bright ? rnd() * 4 + 3 : 0;
    stars.push({
      x, y, r: bright ? r + 0.6 : r,
      fill: bright ? 'oklch(0.95 0.03 230)' : 'oklch(0.82 0.02 250)',
      opacity, twinkle: bright, dur,
    });
  }
  const nebs: Neb[] = SYSTEMS.map((sm, ri) => {
    const nd = 20 + ri * 2.5;
    const dx = (rnd() * 2 - 1) * 7, dy = (rnd() * 2 - 1) * 6;
    const rot = rnd() * 44 - 22, ry = sm.r * (0.78 + rnd() * 0.12);
    const dustN = Math.max(4, Math.round(sm.r / 24));
    const dust: Dust[] = [];
    for (let i = 0; i < dustN; i++) {
      const a = rnd() * Math.PI * 2, rr = Math.sqrt(rnd());
      dust.push({
        x: sm.cx + Math.cos(a) * rr * sm.r * 0.8,
        y: sm.cy + Math.sin(a) * rr * ry * 0.8,
        r: rnd() * 1.1 + 0.5,
        opacity: rnd() * 0.3 + 0.14,
      });
    }
    const fs = Math.max(17, Math.min(78, sm.r * 0.30));
    const big = sm.r >= 150;
    const ly = big ? sm.cy + fs * 0.32 : sm.cy - sm.r * 0.62;
    const labelOpacity = sm.r >= 240 ? 0.5 : big ? 0.82 : 0.92;
    return { sm, nd, dx, dy, rot, ry, dust, fs, ly, labelOpacity };
  });
  return { STARS: stars, NEBULAE: nebs };
})();

const GRID = (() => {
  const v: number[] = [], h: number[] = [];
  for (let gx = 245; gx < W; gx += 245) v.push(gx);
  for (let gy = 213; gy < H; gy += 213) h.push(gy);
  return { v, h };
})();

const FACTION_LEGEND = ['jenquai', 'progen', 'terran', 'pirate', 'contested', 'neutral'];

type Highlight = { kind: 'faction'; f: string } | { kind: 'online' } | null;

function pad4(n: number) { return String(Math.max(0, Math.round(n))).padStart(4, '0'); }

export function Galaxy() {
  const [occ, setOcc] = useState<GalaxyOccupancy | null>(null);
  const [idByNorm, setIdByNorm] = useState<Record<string, string>>({});
  const [err, setErr] = useState<string | null>(null);
  const [hover, setHover] = useState<string | null>(null);
  const [hl, setHl] = useState<Highlight>(null);
  const [tip, setTip] = useState<{ x: number; y: number } | null>(null);

  const wrapRef = useRef<HTMLDivElement>(null);
  const coordsRef = useRef<HTMLDivElement>(null);

  // Topology: fetched once, used only to bridge node name -> DB sector id.
  useEffect(() => {
    let alive = true;
    api.fetchGalaxy()
      .then((m: GalaxyMap) => {
        if (!alive) return;
        const map: Record<string, string> = {};
        for (const s of m.sectors) map[gnorm(s.name)] = String(s.id);
        setIdByNorm(map);
      })
      .catch(e => { if (alive) setErr(e instanceof Error ? e.message : 'failed to load galaxy'); });
    return () => { alive = false; };
  }, []);

  // Occupancy: polled. Keep last good counts on a failed poll.
  useEffect(() => {
    let alive = true;
    const tick = () => api.fetchGalaxyOccupancy()
      .then(o => { if (alive) setOcc(o); })
      .catch(() => { /* keep last good occupancy */ });
    tick();
    const h = setInterval(tick, OCCUPANCY_POLL_MS);
    return () => { alive = false; clearInterval(h); };
  }, []);

  const counts = occ?.counts ?? {};
  // Live pilot count per AUTHORED node: gnorm(name) -> sector id -> count.
  const presence = useMemo(() => {
    const p: Record<string, number> = {};
    for (const s of NODES) {
      const id = idByNorm[gnorm(s.n)];
      p[s.n] = id ? (counts[id] ?? 0) : 0;
    }
    return p;
  }, [idByNorm, counts]);

  const dimall = hover != null || hl != null;
  const total = occ?.total ?? 0;

  // Compute the tooltip pixel position from a node's authored coords, accounting
  // for the SVG's xMidYMid letterboxing inside the (responsive) map frame.
  function placeTip(s: Node) {
    const wrap = wrapRef.current;
    if (!wrap) return;
    const rect = wrap.getBoundingClientRect();
    const scale = Math.min(rect.width / W, rect.height / H);
    const offX = (rect.width - W * scale) / 2;
    const offY = (rect.height - H * scale) / 2;
    let tx = offX + s.x * scale + 18;
    let ty = offY + s.y * scale - 10;
    if (tx > rect.width - 200) tx = offX + s.x * scale - 186;
    if (ty < 8) ty = 8;
    setTip({ x: tx, y: ty });
  }

  function onMove(e: React.MouseEvent) {
    const wrap = wrapRef.current, out = coordsRef.current;
    if (!wrap || !out) return;
    const r = wrap.getBoundingClientRect();
    const vx = ((e.clientX - r.left) / r.width) * W;
    const vy = ((e.clientY - r.top) / r.height) * H;
    out.textContent = `[ ${pad4(vx)} · ${pad4(vy)} ]`;
  }

  const hovered = hover ? byName[hover] : null;

  function nodeHot(s: Node): boolean {
    if (hover === s.n) return true;
    if (hl?.kind === 'faction') return s.f === hl.f;
    if (hl?.kind === 'online') return presence[s.n] > 0;
    return false;
  }

  return (
    <div className="page galaxy">
      <div className="page__head">
        <div>
          <h1 className="page__title">Galaxy</h1>
          <div className="page__sub">
            Live sector chart &middot; <b>{total}</b> pilot{total === 1 ? '' : 's'} online &middot; {NODES.length} charted sectors
          </div>
        </div>
        <div className={'gxlegend' + (hl ? ' dim' : '')}>
          {FACTION_LEGEND.map(f => (
            <span
              key={f}
              className={'gxlegend__item' + (hl?.kind === 'faction' && hl.f === f ? ' on' : '')}
              onMouseEnter={() => setHl({ kind: 'faction', f })}
              onMouseLeave={() => setHl(h => (h?.kind === 'faction' && h.f === f ? null : h))}>
              <span className="gxlegend__sw" style={{ background: node(f) }} />
              {F[f].label}
            </span>
          ))}
          <span
            className={'gxlegend__online' + (hl?.kind === 'online' ? ' on' : '')}
            onMouseEnter={() => setHl({ kind: 'online' })}
            onMouseLeave={() => setHl(h => (h?.kind === 'online' ? null : h))}>
            <span className="gxlegend__pulse" /> Pilots online
          </span>
        </div>
      </div>

      {err && <div className="hud-panel empty">{err}</div>}

      <div className="mapwrap" ref={wrapRef} onMouseMove={onMove}>
        <svg
          className={'map' + (dimall ? ' dimall' : '')}
          viewBox={`0 0 ${W} ${H}`}
          preserveAspectRatio="xMidYMid meet"
          aria-label="Galaxy map">
          <defs>
            <filter id="gx-neb" x="-60%" y="-60%" width="220%" height="220%">
              <feGaussianBlur in="SourceGraphic" stdDeviation="30" />
            </filter>
            <filter id="gx-neb2" x="-60%" y="-60%" width="220%" height="220%">
              <feGaussianBlur in="SourceGraphic" stdDeviation="14" />
            </filter>
            <filter id="gx-glow" x="-120%" y="-120%" width="340%" height="340%">
              <feGaussianBlur stdDeviation="3.4" result="b" />
              <feMerge>
                <feMergeNode in="b" />
                <feMergeNode in="SourceGraphic" />
              </feMerge>
            </filter>
            {Object.keys(F).map(f => (
              <radialGradient key={'neb-' + f} id={'gx-neb-' + f} cx="50%" cy="48%" r="58%">
                <stop offset="0%" stopColor={col(f, 0.55, 0.55)} />
                <stop offset="45%" stopColor={col(f, 0.42, 0.30)} />
                <stop offset="100%" stopColor={col(f, 0.30, 0.0)} />
              </radialGradient>
            ))}
            {Object.keys(F).map(f => (
              <radialGradient key={'core-' + f} id={'gx-core-' + f} cx="42%" cy="38%" r="62%">
                <stop offset="0%" stopColor={col(f, 0.97)} />
                <stop offset="40%" stopColor={node(f)} />
                <stop offset="100%" stopColor={col(f, 0.50)} />
              </radialGradient>
            ))}
          </defs>

          {/* starfield */}
          <g className="l-stars">
            {STARS.map((st, i) => (
              <circle
                key={i} cx={st.x.toFixed(1)} cy={st.y.toFixed(1)} r={st.r.toFixed(2)}
                fill={st.fill} opacity={st.opacity.toFixed(2)}
                className={st.twinkle ? 'twinkle' : undefined}
                style={st.twinkle ? ({ '--d': st.dur.toFixed(1) + 's' } as Vars) : undefined} />
            ))}
          </g>

          {/* nebula territories */}
          <g className="l-neb">
            {NEBULAE.map(nb => (
              <g key={nb.sm.name}>
                <g className="nebula" style={{ '--nd': nb.nd + 's', '--dx': nb.dx.toFixed(1) + 'px', '--dy': nb.dy.toFixed(1) + 'px' } as Vars}>
                  <ellipse
                    cx={nb.sm.cx} cy={nb.sm.cy} rx={nb.sm.r} ry={nb.ry}
                    fill={`url(#gx-neb-${nb.sm.f})`} filter="url(#gx-neb)"
                    transform={`rotate(${nb.rot} ${nb.sm.cx} ${nb.sm.cy})`} />
                  <ellipse
                    cx={nb.sm.cx} cy={nb.sm.cy} rx={nb.sm.r * 0.86} ry={nb.ry * 0.86}
                    fill="none" stroke={col(nb.sm.f, 0.62, 0.16)} strokeWidth={1}
                    filter="url(#gx-neb2)" transform={`rotate(${nb.rot} ${nb.sm.cx} ${nb.sm.cy})`} />
                </g>
                {nb.dust.map((d, i) => (
                  <circle key={i} cx={d.x.toFixed(1)} cy={d.y.toFixed(1)} r={d.r.toFixed(2)}
                    fill={node(nb.sm.f)} opacity={d.opacity.toFixed(2)} />
                ))}
                <text
                  className="reg-label" x={nb.sm.cx} y={nb.ly} fontSize={nb.fs.toFixed(0)}
                  textAnchor="middle"
                  style={{
                    '--rl': col(nb.sm.f, 0.88, 0.95),
                    opacity: nb.labelOpacity,
                    filter: `drop-shadow(0 0 ${(nb.fs * 0.4).toFixed(0)}px ${col(nb.sm.f, 0.6, 0.5)})`,
                  } as Vars}>
                  {nb.sm.name}
                </text>
              </g>
            ))}
          </g>

          {/* faint graticule */}
          <g className="l-grid">
            {GRID.v.map(gx => <line key={'v' + gx} x1={gx} y1={0} x2={gx} y2={H} stroke="oklch(0.5 0.04 250 / 0.05)" strokeWidth={1} />)}
            {GRID.h.map(gy => <line key={'h' + gy} x1={0} y1={gy} x2={W} y2={gy} stroke="oklch(0.5 0.04 250 / 0.05)" strokeWidth={1} />)}
          </g>

          {/* jump lanes */}
          <g className="l-lanes">
            {LANES.map((L, i) => {
              const hot = hover != null && (L.a === hover || L.b === hover);
              return (
                <path
                  key={i} className={'lane' + (L.gate ? ' lane--gate' : '') + (hot ? ' hot' : '')}
                  d={L.d} stroke={L.gate ? 'var(--line-strong)' : undefined} />
              );
            })}
          </g>

          {/* sector nodes */}
          <g className="l-nodes">
            {NODES.map(s => {
              const online = presence[s.n];
              const hot = nodeHot(s);
              const cls = 'node'
                + (s.hub ? ' node--hub' : '')
                + (s.minor ? ' node--minor' : '')
                + (s.special ? ' node--special' : '')
                + (online > 0 ? ' node--online' : '')
                + (hot ? ' hot' : '');
              const R = s.R;
              const above = s.y > 1150;
              return (
                <g key={s.n} className={cls} tabIndex={0}
                  onMouseEnter={() => { setHover(s.n); placeTip(s); }}
                  onMouseLeave={() => { setHover(h => (h === s.n ? null : h)); setTip(null); }}
                  onFocus={() => { setHover(s.n); placeTip(s); }}
                  onBlur={() => { setHover(h => (h === s.n ? null : h)); setTip(null); }}>
                  {/* soft halo */}
                  <circle cx={s.x} cy={s.y} r={R * 2.6} fill={glowc(s.f)} filter="url(#gx-neb2)" opacity={0.5} />
                  {/* pulsing ring for hubs */}
                  {s.hub && (
                    <circle className="pulse" cx={s.x} cy={s.y} r={R} fill="none" stroke={node(s.f)} strokeWidth={1.6}
                      opacity={0.5} style={{ animationDelay: ((s.x % 30) / 10).toFixed(1) + 's' }} />
                  )}
                  {/* namesake accent on Freya */}
                  {s.special && (
                    <circle cx={s.x} cy={s.y} r={R + 7} fill="none" stroke="var(--accent)" strokeWidth={1.4}
                      opacity={0.7} filter="url(#gx-glow)" />
                  )}
                  {/* glow body */}
                  <circle cx={s.x} cy={s.y} r={R + 2} fill={glowc(s.f)} filter="url(#gx-glow)" opacity={0.6} />
                  {/* core */}
                  <circle
                    className="node__core" cx={s.x} cy={s.y} r={hot ? R + 1.6 : R}
                    fill={`url(#gx-core-${s.f})`}
                    stroke={s.special ? 'var(--accent)' : s.hub ? col(s.f, 0.95, 0.9) : 'none'}
                    strokeWidth={s.hub || s.special ? 1.4 : 0} />
                  {/* label */}
                  {!s.minor && (
                    <text
                      className="node__label" x={s.x}
                      y={above ? s.y - R - 10 : s.y + R + (s.hub ? 26 : 17)}
                      textAnchor="middle" fontSize={s.hub ? 22 : 13}>
                      {s.n}
                    </text>
                  )}
                  {/* live presence: sonar waves + count */}
                  {online > 0 && [0, 1, 2].map(w => (
                    <circle key={'w' + w} className="wave" cx={s.x} cy={s.y} r={R + 3} fill="none"
                      stroke="var(--presence)" strokeWidth={online > 4 ? 2.2 : 1.5}
                      style={{ animationDelay: (w * 0.95).toFixed(2) + 's' }} />
                  ))}
                  {online > 1 && (
                    <text className="count" x={s.x + R + 7} y={s.y - R - 4} fontSize={s.hub ? 19 : 16}>{online}</text>
                  )}
                </g>
              );
            })}
          </g>
        </svg>

        {/* bottom-left readout */}
        <div className="readout">
          <div>SECTOR GRID &middot; <b>FREYA-9</b></div>
          <div className="readout__crosshair" ref={coordsRef}>[ 0000 &middot; 0000 ]</div>
        </div>

        {/* tooltip */}
        {hovered && tip && (
          <div
            className="tip show"
            style={{ left: tip.x, top: tip.y, '--tc': col(hovered.f, 0.74) } as Vars}>
            <div className="tip__bar" />
            <div className="tip__in">
              <div className="tip__name">{hovered.n}</div>
              <div className="tip__faction">{F[hovered.f].label} Space</div>
              <div className="tip__rows">
                {presence[hovered.n] > 0 && (
                  <div className="tip__row">
                    <span>Pilots online</span>
                    <span style={{ color: 'var(--presence)' }}>{presence[hovered.n]}</span>
                  </div>
                )}
                <div className="tip__row"><span>System</span><span>{hovered.sys}</span></div>
                <div className="tip__row"><span>Type</span><span>{hovered.type}</span></div>
                <div className="tip__row"><span>Coord</span><span>{pad4(hovered.x)} &middot; {pad4(hovered.y)}</span></div>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
