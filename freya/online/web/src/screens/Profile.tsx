// SPDX-License-Identifier: MIT
// Freya Online -- Profile page.
//
// The first tab. Shows the SELECTED character (driven by the topbar avatar
// dropdown): identity (overall level, stylized class), the three discipline
// bars (combat/explore/trade in teal/blue/purple), the starship card with hull
// integrity, cargo, drive speeds and every fitted item (hover for the full
// tooltip), the current sector, and the learned skills as filled/empty dots.
//
// Data loads on demand for one character at a time via three small endpoints
// (profile / ship / skills); switching characters re-pulls only that character.

import { useEffect, useState } from 'react';
import type { CSSProperties } from 'react';
import type { AvatarProfile, EquippedItem, ShipView, SkillView } from '../types';
import * as api from '../api';
import { ItemIcon } from '../components/ui';
import { ItemDisplay } from '../components/ItemDisplay';

type Vars = CSSProperties & Record<string, string | number>;

// Discipline accent colors: combat teal, explore blue, trade purple.
const DISC = {
  combat: { label: 'Combat', color: 'oklch(0.80 0.135 178)' },
  explore: { label: 'Explore', color: 'oklch(0.70 0.15 250)' },
  trade: { label: 'Trade', color: 'oklch(0.68 0.18 305)' },
} as const;

function DisciplineBar({
  kind, level, bar,
}: { kind: keyof typeof DISC; level: number; bar: number }) {
  const d = DISC[kind];
  const pct = Math.round(Math.max(0, Math.min(1, bar)) * 100);
  return (
    <div className="disc" style={{ '--bandc': d.color } as Vars}>
      <div className="disc__top">
        <span className="disc__label">{d.label}</span>
        <span className="disc__lvl">{level}</span>
      </div>
      <div className="disc__track">
        <div className="disc__fill" style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

// Filled/empty dots: `level` of `max` filled. Caps the rendered dot count so a
// pathological max never blows out the row.
function SkillDots({ level, max }: { level: number; max: number }) {
  const total = Math.max(level, Math.min(max, 12));
  const dots = [];
  for (let i = 0; i < total; i++) {
    dots.push(<span key={i} className={'sdot' + (i < level ? ' sdot--on' : '')} />);
  }
  return <span className="sdots" title={`Rank ${level}`}>{dots}</span>;
}

function EquipTile({ eq }: { eq: EquippedItem }) {
  return (
    <div className="ehover">
      <div className="ehover__tile">
        <ItemIcon item={eq.item} className="ehover__icon" />
        <div className="ehover__meta">
          <div className="ehover__name">{eq.item.name}</div>
          <div className="ehover__slot">{eq.item.slot}</div>
        </div>
      </div>
      <div className="ehover__pop">
        <ItemDisplay item={eq.item} quality={eq.quality} />
      </div>
    </div>
  );
}

// The skill categories in display order. "Total" skills (cross-discipline, e.g.
// Build Items) sort last under a neutral heading.
const SKILL_ORDER = ['Combat', 'Explore', 'Trade', 'Total'];

export function Profile({ character }: { character: string }) {
  const [profile, setProfile] = useState<AvatarProfile | null>(null);
  const [ship, setShip] = useState<ShipView | null>(null);
  const [skills, setSkills] = useState<SkillView[] | null>(null);
  const [err, setErr] = useState<string | null>(null);

  // Re-pull all three slices whenever the selected character changes. `alive`
  // guards against a slow response landing after another character was picked.
  useEffect(() => {
    let alive = true;
    setProfile(null); setShip(null); setSkills(null); setErr(null);
    Promise.all([
      api.fetchProfile(character).then(p => { if (alive) setProfile(p); }),
      api.fetchShip(character).then(s => { if (alive) setShip(s); }),
      api.fetchSkills(character).then(s => { if (alive) setSkills(s); }),
    ]).catch(e => { if (alive) setErr(e instanceof Error ? e.message : 'failed to load'); });
    return () => { alive = false; };
  }, [character]);

  const grouped = SKILL_ORDER
    .map(cat => ({ cat, list: (skills ?? []).filter(s => s.category === cat) }))
    .filter(g => g.list.length > 0);

  return (
    <div className="page profile">
      <div className="page__head">
        <div>
          <h1 className="page__title">Profile</h1>
          <div className="page__sub">Pilot dossier &middot; {character.replace(/_/g, ' ')}</div>
        </div>
      </div>

      {err && <div className="hud-panel empty profile__err">{err}</div>}

      <div className="profile__body">
        {/* Identity + disciplines */}
        <section className="hud-panel hud-corners pcard pcard--id">
          <div className="pid">
            <div className="pid__level">
              <span className="pid__lvlnum">{profile?.level ?? '--'}</span>
              <span className="pid__lvllabel">Overall Level</span>
            </div>
            <div className="pid__name">
              <div className="disp pid__char">{character.replace(/_/g, ' ')}</div>
              <div className="pid__class">{profile?.class ?? ''}</div>
              <div className="pid__loc">
                <span className="pid__locglyph">◎</span>
                {profile?.sector ? profile.sector : 'Unknown space'}
              </div>
            </div>
          </div>

          <div className="pdiscs">
            {profile && <>
              <DisciplineBar kind="combat" level={profile.combat.level} bar={profile.combat.bar} />
              <DisciplineBar kind="explore" level={profile.explore.level} bar={profile.explore.bar} />
              <DisciplineBar kind="trade" level={profile.trade.level} bar={profile.trade.bar} />
            </>}
          </div>
        </section>

        {/* Starship */}
        <section className="hud-panel hud-corners pcard pcard--ship">
          <div className="pcard__title">Starship</div>
          <div className="pship__name">{ship?.name?.trim() || 'Unnamed ship'}</div>

          <div className="pship__stats">
            <Stat label="Hull"
              value={ship ? String(Math.round(ship.maxHullPoints)) : '--'} />
            <Stat label="Cargo" value={ship ? `${ship.cargoSpace} slots` : '--'} />
            <Stat label="Warp" value={ship ? String(ship.warp) : '--'} />
          </div>
          {ship && ship.maxHullPoints > 0 && (
            <div className="phull">
              <div className="phull__fill"
                style={{ width: `${Math.round(Math.min(1, ship.hullPoints / ship.maxHullPoints) * 100)}%` }} />
            </div>
          )}

          <div className="pcard__subtitle">Equipped</div>
          {ship && ship.equipment.length > 0 ? (
            <div className="pequip">
              {ship.equipment.map(eq => <EquipTile key={eq.slot} eq={eq} />)}
            </div>
          ) : (
            <div className="pequip__empty">{ship ? 'No equipment fitted.' : 'Loading...'}</div>
          )}
        </section>

        {/* Skills */}
        <section className="hud-panel hud-corners pcard pcard--skills">
          <div className="pcard__title">Skills &amp; Abilities</div>
          {skills === null ? (
            <div className="pequip__empty">Loading...</div>
          ) : grouped.length === 0 ? (
            <div className="pequip__empty">No skills learned yet.</div>
          ) : (
            grouped.map(g => (
              <div className="pskillgrp" key={g.cat}>
                <div className="pskillgrp__head">{g.cat}</div>
                {g.list.map(s => (
                  <div className="pskill" key={s.id}>
                    <span className="pskill__name">{s.name}</span>
                    <SkillDots level={s.level} max={s.maxLevel} />
                  </div>
                ))}
              </div>
            ))
          )}
        </section>
      </div>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="pstat">
      <span className="pstat__label">{label}</span>
      <span className="pstat__value">{value}</span>
    </div>
  );
}
