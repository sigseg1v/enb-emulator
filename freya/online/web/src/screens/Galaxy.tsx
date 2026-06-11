// SPDX-License-Identifier: MIT
// Freya Online -- Galaxy map (first tab).
//
// A stylized, auto-laid-out map of every named sector in the game, grouped into
// its star system and linked by the gate graph. Each sector lights up and glows
// by how many players are currently in it -- normalized RELATIVE to the busiest
// sector, so one player versus two reads the same as fifty versus a hundred
// (the brightest sector is always full-bright, everything else is a fraction of
// it). The topology (/api/galaxy) is fetched once; the live occupancy
// (/api/galaxy/occupancy) is polled on a short cadence to drive the glow.
//
// There is no real coordinate data in the content DB (galaxy_x/galaxy_y are all
// zero), so the layout is deterministic and force-free: systems are spread
// around a ring, and each system's sectors fan out in a sub-ring around it. No
// Math.random -- the same map always lays out identically.

import { useEffect, useMemo, useState } from 'react';
import type { CSSProperties } from 'react';
import type { GalaxyMap, GalaxyOccupancy, GalaxySector } from '../types';
import * as api from '../api';

type Vars = CSSProperties & Record<string, string | number>;

const OCCUPANCY_POLL_MS = 15_000;
const VIEW = 1000; // SVG viewBox is VIEW x VIEW

// Faction accent colors. Tokens come from the backend's normalizeFaction.
const FACTION: Record<string, { color: string; label: string }> = {
  jenquai: { color: 'oklch(0.70 0.15 250)', label: 'Jenquai' },
  progen: { color: 'oklch(0.66 0.20 25)', label: 'Progen' },
  terran: { color: 'oklch(0.80 0.14 145)', label: 'Terran' },
  pirate: { color: 'oklch(0.74 0.17 60)', label: 'Pirate' },
  contested: { color: 'oklch(0.68 0.18 305)', label: 'Contested' },
  neutral: { color: 'oklch(0.80 0.04 230)', label: 'Neutral' },
  deepspace: { color: 'oklch(0.58 0.03 250)', label: 'Deep Space' },
};
function factionColor(f: string): string {
  return (FACTION[f] ?? FACTION.deepspace).color;
}

interface Placed { sector: GalaxySector; x: number; y: number; }
interface SysLabel { id: number; name: string; faction: string; x: number; y: number; }
interface Layout { nodes: Placed[]; byId: Map<number, Placed>; labels: SysLabel[]; }

// Deterministic radial clustering: each star system gets a slot on the main
// ring (ordered by id for stability), and its sectors fan out around that slot
// on a sub-ring whose radius grows with the sector count. Sectors with no
// system collapse into a single outer "deep space" cluster (key 0).
function buildLayout(map: GalaxyMap): Layout {
  const cx = VIEW / 2, cy = VIEW / 2, ringR = 360;
  const sysFaction = new Map<number, string>();
  const sysName = new Map<number, string>();
  for (const s of map.systems) { sysFaction.set(s.id, s.faction); sysName.set(s.id, s.name); }

  const clusters = new Map<number, GalaxySector[]>();
  for (const sec of map.sectors) {
    const key = sysName.has(sec.systemId) ? sec.systemId : 0;
    const arr = clusters.get(key);
    if (arr) arr.push(sec); else clusters.set(key, [sec]);
  }

  const keys = [...clusters.keys()].sort((a, b) => a - b);
  const nodes: Placed[] = [];
  const byId = new Map<number, Placed>();
  const labels: SysLabel[] = [];

  keys.forEach((key, ci) => {
    const secs = clusters.get(key)!.slice().sort((a, b) => a.id - b.id);
    const ang = (ci / keys.length) * Math.PI * 2 - Math.PI / 2;
    const sx = cx + Math.cos(ang) * ringR;
    const sy = cy + Math.sin(ang) * ringR;
    const n = secs.length;
    const subR = Math.min(130, 16 + Math.sqrt(n) * 24);

    secs.forEach((sec, si) => {
      let x = sx, y = sy;
      if (n > 1) {
        const a2 = (si / n) * Math.PI * 2 + ci; // ci offset so clusters don't align
        const rr = subR * (0.55 + 0.45 * ((si % 3) / 2));
        x = sx + Math.cos(a2) * rr;
        y = sy + Math.sin(a2) * rr;
      }
      const p: Placed = { sector: sec, x, y };
      nodes.push(p);
      byId.set(sec.id, p);
    });

    if (key !== 0) {
      labels.push({
        id: key, name: sysName.get(key) ?? '', faction: sysFaction.get(key) ?? 'deepspace',
        // Push the label slightly outward from the cluster center.
        x: cx + Math.cos(ang) * (ringR + subR + 26),
        y: cy + Math.sin(ang) * (ringR + subR + 26),
      });
    }
  });

  return { nodes, byId, labels };
}

export function Galaxy() {
  const [map, setMap] = useState<GalaxyMap | null>(null);
  const [occ, setOcc] = useState<GalaxyOccupancy | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [hover, setHover] = useState<number | null>(null);

  // Topology: fetched once on mount.
  useEffect(() => {
    let alive = true;
    api.fetchGalaxy()
      .then(m => { if (alive) setMap(m); })
      .catch(e => { if (alive) setErr(e instanceof Error ? e.message : 'failed to load galaxy'); });
    return () => { alive = false; };
  }, []);

  // Occupancy: polled. `alive` guards a late response after unmount.
  useEffect(() => {
    let alive = true;
    const tick = () => api.fetchGalaxyOccupancy()
      .then(o => { if (alive) setOcc(o); })
      .catch(() => { /* keep the last good occupancy; topology still renders */ });
    tick();
    const h = setInterval(tick, OCCUPANCY_POLL_MS);
    return () => { alive = false; clearInterval(h); };
  }, []);

  const layout = useMemo(() => (map ? buildLayout(map) : null), [map]);
  const counts = occ?.counts ?? {};
  // Relative-max normalization: the busiest sector defines full brightness.
  const maxCount = useMemo(() => {
    let m = 0;
    for (const k in counts) if (counts[k] > m) m = counts[k];
    return m;
  }, [counts]);

  const hovered = hover != null && layout ? layout.byId.get(hover) ?? null : null;
  const hoveredCount = hovered ? (counts[String(hovered.sector.id)] ?? 0) : 0;

  return (
    <div className="page galaxy">
      <div className="page__head">
        <div>
          <h1 className="page__title">Galaxy</h1>
          <div className="page__sub">
            Live sector map &middot; {occ ? `${occ.total} pilot${occ.total === 1 ? '' : 's'} online` : 'syncing...'}
          </div>
        </div>
        <div className="gxlegend">
          {Object.entries(FACTION).map(([k, v]) => (
            <span className="gxlegend__item" key={k}>
              <span className="gxlegend__dot" style={{ background: v.color }} /> {v.label}
            </span>
          ))}
        </div>
      </div>

      {err && <div className="hud-panel empty">{err}</div>}

      {!layout ? (
        <div className="hud-panel empty">Charting the galaxy...</div>
      ) : (
        <div className="hud-panel hud-corners galaxy__stage">
          <svg className="galaxy__svg" viewBox={`0 0 ${VIEW} ${VIEW}`} preserveAspectRatio="xMidYMid meet">
            {/* Gate edges, drawn under the nodes. */}
            <g className="galaxy__edges">
              {map!.edges.map((e, i) => {
                const a = layout.byId.get(e.from), b = layout.byId.get(e.to);
                if (!a || !b) return null;
                return <line key={i} x1={a.x} y1={a.y} x2={b.x} y2={b.y} />;
              })}
            </g>

            {/* System labels. */}
            <g className="galaxy__labels">
              {layout.labels.map(l => (
                <text key={l.id} x={l.x} y={l.y} fill={factionColor(l.faction)}
                  textAnchor="middle" dominantBaseline="middle">{l.name}</text>
              ))}
            </g>

            {/* Sector nodes. Radius + glow scale with relative occupancy. */}
            <g className="galaxy__nodes">
              {layout.nodes.map(p => {
                const c = counts[String(p.sector.id)] ?? 0;
                const intensity = maxCount > 0 && c > 0 ? c / maxCount : 0;
                const col = factionColor(p.sector.faction);
                const r = 5 + intensity * 9;
                const isHover = hover === p.sector.id;
                return (
                  <g key={p.sector.id} className="galaxy__node"
                    onMouseEnter={() => setHover(p.sector.id)}
                    onMouseLeave={() => setHover(h => (h === p.sector.id ? null : h))}>
                    {/* Glow halo only when occupied. */}
                    {intensity > 0 && (
                      <circle cx={p.x} cy={p.y} r={r + 10 + intensity * 16}
                        fill={col} opacity={0.10 + intensity * 0.30} className="galaxy__glow" />
                    )}
                    <circle cx={p.x} cy={p.y} r={isHover ? r + 2 : r}
                      fill={col} stroke={col}
                      style={{ '--gx-int': intensity } as Vars}
                      opacity={0.35 + intensity * 0.65} className="galaxy__dot" />
                  </g>
                );
              })}
            </g>
          </svg>

          {hovered && (
            <div className="galaxy__info">
              <div className="galaxy__info-name">{hovered.sector.name}</div>
              <div className="galaxy__info-sys">
                {hovered.sector.system || 'Uncharted'}
                <span className="galaxy__info-fac" style={{ color: factionColor(hovered.sector.faction) }}>
                  {(FACTION[hovered.sector.faction] ?? FACTION.deepspace).label}
                </span>
              </div>
              <div className="galaxy__info-pop">
                <b>{hoveredCount}</b> pilot{hoveredCount === 1 ? '' : 's'} here
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
