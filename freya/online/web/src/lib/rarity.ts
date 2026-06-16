// SPDX-License-Identifier: MIT
// Freya Online -- rarity model.
//
// Rarity is derived from an item's quality, then CAPPED by its level. This is
// the single client-side mirror of the rule the Go service applies server-side;
// both must agree. The server is authoritative -- this exists so the dev mock
// and any optimistic UI render the same colour the backend would assign.
//
// Quality is the game-native FRACTION (1.0 == 100%), so thresholds are fractional:
//   quality >= 1.80 -> epic
//   quality >= 1.50 -> rare
//   quality >= 1.30 -> uncommon
//   quality <  1.30 -> common  (includes < 1.00 "junk")
//   no quality      -> always common
//
// Level caps (a low-level item can never read as a high tier):
//   level 1-3 -> capped at uncommon
//   level 4-6 -> capped at rare
//   level 7+  -> may reach epic

import type { RarityKey } from '../types';

export interface RarityDef {
    key: RarityKey;
    label: string;
    color: string;
    glow: string;
}

export const RARITY: Record<RarityKey, RarityDef> = {
    common: {
        key: 'common',
        label: 'Common',
        color: 'var(--r-common)',
        glow: 'var(--r-common-glow)',
    },
    uncommon: {
        key: 'uncommon',
        label: 'Uncommon',
        color: 'var(--r-uncommon)',
        glow: 'var(--r-uncommon-glow)',
    },
    rare: { key: 'rare', label: 'Rare', color: 'var(--r-rare)', glow: 'var(--r-rare-glow)' },
    epic: { key: 'epic', label: 'Epic', color: 'var(--r-epic)', glow: 'var(--r-epic-glow)' },
    legendary: {
        key: 'legendary',
        label: 'Legendary',
        color: 'var(--r-legendary)',
        glow: 'var(--r-legendary-glow)',
    },
};

const ORDER: RarityKey[] = ['common', 'uncommon', 'rare', 'epic', 'legendary'];

export function rarityFor(level: number, quality: number | null): RarityKey {
    if (quality == null) return 'common';

    let tier: RarityKey;
    if (quality >= 1.8) tier = 'epic';
    else if (quality >= 1.5) tier = 'rare';
    else if (quality >= 1.3) tier = 'uncommon';
    else tier = 'common';

    let cap: RarityKey;
    if (level <= 3) cap = 'uncommon';
    else if (level <= 6) cap = 'rare';
    else cap = 'epic';

    return ORDER.indexOf(tier) > ORDER.indexOf(cap) ? cap : tier;
}

export function qualityClass(q: number): string {
    if (q >= 1.7) return 'qual--exc';
    if (q >= 1.3) return 'qual--high';
    if (q >= 1.0) return 'qual--ok';
    return 'qual--low';
}
