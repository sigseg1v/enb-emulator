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
import styles from './Profile.module.css';

type Vars = CSSProperties & Record<string, string | number>;

// Discipline accent colors: combat teal, explore blue, trade purple.
const DISC = {
    combat: { label: 'Combat', color: 'oklch(0.80 0.135 178)' },
    explore: { label: 'Explore', color: 'oklch(0.70 0.15 250)' },
    trade: { label: 'Trade', color: 'oklch(0.68 0.18 305)' },
} as const;

function DisciplineBar({
    kind,
    level,
    bar,
}: {
    kind: keyof typeof DISC;
    level: number;
    bar: number;
}) {
    const d = DISC[kind];
    const pct = Math.round(Math.max(0, Math.min(1, bar)) * 100);
    return (
        <div style={{ '--bandc': d.color } as Vars}>
            <div className={styles.discTop}>
                <span className={styles.discLabel}>{d.label}</span>
                <span className={styles.discLvl}>{level}</span>
            </div>
            <div className={styles.discTrack}>
                <div className={styles.discFill} style={{ width: `${pct}%` }} />
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
        dots.push(
            <span key={i} className={styles.sdot + (i < level ? ' ' + styles.sdotOn : '')} />,
        );
    }
    return (
        <span className={styles.sdots} title={`Rank ${level}`}>
            {dots}
        </span>
    );
}

function EquipTile({ eq }: { eq: EquippedItem }) {
    return (
        <div className={styles.ehover}>
            <div className={styles.ehoverTile}>
                <ItemIcon item={eq.item} />
                <div className={styles.ehoverMeta}>
                    <div className={styles.ehoverName}>{eq.item.name}</div>
                    <div className={styles.ehoverSlot}>{eq.item.slot}</div>
                </div>
            </div>
            <div className={styles.ehoverPop}>
                <ItemDisplay item={eq.item} quality={eq.quality} />
            </div>
        </div>
    );
}

// The skill categories in display order. "Total" skills (cross-discipline, e.g.
// Build Items) sort last under a neutral heading.
const SKILL_ORDER = ['Combat', 'Explore', 'Trade', 'Equipment Tech', 'Total'];

export function Profile({ character }: { character: string }) {
    const [profile, setProfile] = useState<AvatarProfile | null>(null);
    const [ship, setShip] = useState<ShipView | null>(null);
    const [skills, setSkills] = useState<SkillView[] | null>(null);
    const [err, setErr] = useState<string | null>(null);

    // Re-pull all three slices whenever the selected character changes. `alive`
    // guards against a slow response landing after another character was picked.
    useEffect(() => {
        let alive = true;
        setProfile(null);
        setShip(null);
        setSkills(null);
        setErr(null);
        Promise.all([
            api.fetchProfile(character).then(p => {
                if (alive) setProfile(p);
            }),
            api.fetchShip(character).then(s => {
                if (alive) setShip(s);
            }),
            api.fetchSkills(character).then(s => {
                if (alive) setSkills(s);
            }),
        ]).catch(e => {
            if (alive) setErr(e instanceof Error ? e.message : 'failed to load');
        });
        return () => {
            alive = false;
        };
    }, [character]);

    const grouped = SKILL_ORDER.map(cat => ({
        cat,
        list: (skills ?? []).filter(s => s.category === cat),
    })).filter(g => g.list.length > 0);

    return (
        <div className={`page ${styles.profile}`}>
            <div className="page__head">
                <div>
                    <h1 className="page__title">Profile</h1>
                    <div className="page__sub">
                        Pilot dossier &middot; {character.replace(/_/g, ' ')}
                    </div>
                </div>
            </div>

            {err && <div className={`hud-panel empty ${styles.profileErr}`}>{err}</div>}

            <div className={styles.profileBody}>
                {/* Identity + disciplines */}
                <section className={`hud-panel hud-corners ${styles.pcard} ${styles.pcardId}`}>
                    <div className={styles.pid}>
                        <div className={styles.pidLevel}>
                            <span className={styles.pidLvlnum}>{profile?.level ?? '--'}</span>
                            <span className={styles.pidLvllabel}>Overall Level</span>
                        </div>
                        <div className={styles.pidName}>
                            <div className={`disp ${styles.pidChar}`}>
                                {character.replace(/_/g, ' ')}
                            </div>
                            <div className={styles.pidClass}>{profile?.class ?? ''}</div>
                            <div className={styles.pidLoc}>
                                <span className={styles.pidLocglyph}>◎</span>
                                {profile?.sector ? profile.sector : 'Unknown space'}
                            </div>
                        </div>
                    </div>

                    <div className={styles.pdiscs}>
                        {profile && (
                            <>
                                <DisciplineBar
                                    kind="combat"
                                    level={profile.combat.level}
                                    bar={profile.combat.bar}
                                />
                                <DisciplineBar
                                    kind="explore"
                                    level={profile.explore.level}
                                    bar={profile.explore.bar}
                                />
                                <DisciplineBar
                                    kind="trade"
                                    level={profile.trade.level}
                                    bar={profile.trade.bar}
                                />
                            </>
                        )}
                    </div>
                </section>

                {/* Starship */}
                <section className={`hud-panel hud-corners ${styles.pcard} ${styles.pcardShip}`}>
                    <div className={styles.pcardTitle}>Starship</div>
                    <div className={styles.pshipName}>{ship?.name?.trim() || 'Unnamed ship'}</div>

                    <div className={styles.pshipStats}>
                        <Stat
                            label="Hull"
                            value={ship ? String(Math.round(ship.maxHullPoints)) : '--'}
                        />
                        <Stat label="Cargo" value={ship ? `${ship.cargoSpace} slots` : '--'} />
                        <Stat label="Warp" value={ship ? String(ship.warp) : '--'} />
                    </div>
                    {ship && ship.maxHullPoints > 0 && (
                        <div className={styles.phull}>
                            <div
                                className={styles.phullFill}
                                style={{
                                    width: `${Math.round(Math.min(1, ship.hullPoints / ship.maxHullPoints) * 100)}%`,
                                }}
                            />
                        </div>
                    )}

                    <div className={styles.pcardSubtitle}>Equipped</div>
                    {ship && ship.equipment.length > 0 ? (
                        <div className={styles.pequip}>
                            {ship.equipment.map(eq => (
                                <EquipTile key={eq.slot} eq={eq} />
                            ))}
                        </div>
                    ) : (
                        <div className={styles.pequipEmpty}>
                            {ship ? 'No equipment fitted.' : 'Loading...'}
                        </div>
                    )}
                </section>

                {/* Skills */}
                <section className={`hud-panel hud-corners ${styles.pcard} ${styles.pcardSkills}`}>
                    <div className={styles.pcardTitle}>Skills &amp; Abilities</div>
                    {skills === null ? (
                        <div className={styles.pequipEmpty}>Loading...</div>
                    ) : grouped.length === 0 ? (
                        <div className={styles.pequipEmpty}>No skills learned yet.</div>
                    ) : (
                        grouped.map(g => (
                            <div className={styles.pskillgrp} key={g.cat}>
                                <div className={styles.pskillgrpHead}>{g.cat}</div>
                                {g.list.map(s => (
                                    <div className={styles.pskill} key={s.id}>
                                        <span className={styles.pskillName}>{s.name}</span>
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
        <div className={styles.pstat}>
            <span className={styles.pstatLabel}>{label}</span>
            <span className={styles.pstatValue}>{value}</span>
        </div>
    );
}
