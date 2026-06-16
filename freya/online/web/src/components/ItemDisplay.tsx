// SPDX-License-Identifier: MIT
// Freya Online -- full in-game item tooltip.
import type { CSSProperties } from 'react';
import type { Item } from '../types';
import { RARITY } from '../lib/rarity';
import { Credits, ItemIcon, Quality } from './ui';

type Vars = CSSProperties & Record<string, string | number>;

export function ItemDisplay({
    item,
    quality,
    compact = false,
}: {
    item: Item;
    quality?: number | null;
    compact?: boolean;
}) {
    const r = RARITY[item.rarity];
    const isWeapon = item.cat === 'weapon';
    return (
        <div className="idisp" style={{ '--rcolor': r.color, '--rglow': r.glow } as Vars}>
            <div className="idisp__head">
                <ItemIcon item={item} className="idisp__icon" />
                <div style={{ minWidth: 0 }}>
                    <div className="idisp__name">{item.name}</div>
                    <div className="idisp__sub">
                        Level {item.level} &middot; {item.slot} &middot; <b>{r.label}</b>
                        {quality != null && (
                            <>
                                {' '}
                                &middot; Quality <Quality value={quality} />
                            </>
                        )}
                    </div>
                </div>
            </div>

            <div className="idisp__body">
                <div className="idisp__sec">
                    <div className="idisp__sectitle">Description</div>
                    <div className="idisp__desc">{item.description}</div>
                </div>

                {item.buffs && item.buffs.length > 0 && (
                    <div className="idisp__sec">
                        <div className="idisp__sectitle">Bonus / Buffs</div>
                        {item.buffs.map((b, i) => (
                            <div className="buff" key={i}>
                                <div>
                                    <span className="buff__name">{b.name}</span>
                                    <span className="buff__scope">{b.scope}</span>
                                </div>
                                <div className="buff__text">{b.text}</div>
                            </div>
                        ))}
                    </div>
                )}

                {isWeapon && item.ammo && (
                    <div className="idisp__sec">
                        <div className="idisp__sectitle">Ammo</div>
                        <table className="atable">
                            <thead>
                                <tr>
                                    <th>Name</th>
                                    <th>Dmg / shot</th>
                                    <th>DPS</th>
                                    <th>Range</th>
                                    <th>Type</th>
                                </tr>
                            </thead>
                            <tbody>
                                {item.ammo.map((a, i) => (
                                    <tr key={i}>
                                        <td>{a.name}</td>
                                        <td>{a.dmg}</td>
                                        <td>{a.dps}</td>
                                        <td>{a.range}</td>
                                        <td>{a.type}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}

                {item.stats && item.stats.length > 0 && (
                    <div className="idisp__sec">
                        <div className="idisp__sectitle">{isWeapon ? 'Firing' : 'Stats'}</div>
                        <div className="statgrid">
                            {item.stats.map((s, i) => (
                                <div className="statrow" key={i}>
                                    <span>{s.label}</span>
                                    <span>{s.value}</span>
                                </div>
                            ))}
                        </div>
                    </div>
                )}

                {!compact && (
                    <div className="idisp__sec">
                        <div className="idisp__sectitle">More information</div>
                        <div className="idisp__prices">
                            <div className="priceline">
                                <span>Buy price</span>
                                <b>
                                    <Credits value={item.prices.buy} />
                                </b>
                            </div>
                            <div className="priceline">
                                <span>Sell price</span>
                                <b>
                                    <Credits value={item.prices.sell} />
                                </b>
                            </div>
                            {item.prices.manufacture > 0 && (
                                <div className="priceline">
                                    <span>Manufacture cost</span>
                                    <b>
                                        <Credits value={item.prices.manufacture} />
                                    </b>
                                </div>
                            )}
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
}
