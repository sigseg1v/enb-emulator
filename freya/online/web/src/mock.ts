// SPDX-License-Identifier: MIT
// Freya Online -- dev mock dataset.
//
// Mirrors the Claude Design prototype data so the SPA renders identically with
// no backend running. The real Go service returns the same JSON shapes (see
// src/types.ts). Per-entry rarity is computed with the shared rarityFor() rule
// rather than hard-coded, so the mock obeys the same spec the server will.

import type {
    Item,
    Listing,
    MyListing,
    VaultSlot,
    Mail,
    Session,
    ServerStatus,
    AvatarProfile,
    ShipView,
    SkillView,
    GalaxyMap,
    GalaxyOccupancy,
    AvatarLocation,
} from './types';
import { rarityFor } from './lib/rarity';

type BaseItem = Omit<Item, 'rarity'>;

const CATALOG: Record<string, BaseItem> = {
    claimjumper: {
        id: 'claimjumper',
        name: "Claimjumper's Reward Seven",
        cat: 'weapon',
        slot: 'Missile Launcher',
        level: 7,
        glyph: '◈',
        description:
            'A clear message to those who would claim what belongs to the Belters. The weapon delivers a powerful punch.',
        buffs: [
            {
                name: 'Equipment Engineering',
                scope: 'Equip',
                text: 'Reduces equipment install time by 56 (74)% when equipped.',
            },
            {
                name: 'Improved Missile Handling',
                scope: 'Equip',
                text: 'Improved Missile Accuracy by 2.70 (3.59) Skill Level(s) when equipped.',
            },
            {
                name: 'Recharge Reactor',
                scope: 'Equip',
                text: '+5.3 (+7.0) Reactor Recharge when equipped.',
            },
        ],
        ammo: [
            {
                name: "Miner's Fury -- Explosive (L7)",
                dmg: '360 (468.00)',
                dps: '27.48 (54.96)',
                range: '5500',
                type: 'Explosive',
            },
            {
                name: "Miner's Fury -- Plasma (L7)",
                dmg: '360 (468.00)',
                dps: '27.48 (54.96)',
                range: '5500',
                type: 'Plasma',
            },
        ],
        stats: [
            { label: 'Rounds fired', value: '1' },
            { label: 'Reload time', value: '13.10 (8.52)' },
            { label: 'Energy', value: '107' },
            { label: 'EPS', value: '8.17 (12.57)' },
        ],
        prices: { buy: 508781, sell: 39137, manufacture: 69696 },
        vendor: 39137,
    },
    voidlance: {
        id: 'voidlance',
        name: 'Voidlance Beam Projector',
        cat: 'weapon',
        slot: 'Beam Weapon',
        level: 8,
        glyph: '⊳',
        description:
            'A focused coherent-particle lance favored by Jenquai patrol wings. Sustained fire melts hull plating.',
        buffs: [
            {
                name: 'Beam Specialization',
                scope: 'Equip',
                text: '+1.8 (2.4) Beam Skill Level(s) when equipped.',
            },
            {
                name: 'Targeting Uplink',
                scope: 'Equip',
                text: 'Improved critical chance by 4 (6)% when equipped.',
            },
        ],
        ammo: [
            {
                name: 'Coherent Plasma Charge (L8)',
                dmg: '210 (273.00)',
                dps: '42.00 (84.00)',
                range: '4200',
                type: 'Plasma',
            },
        ],
        stats: [
            { label: 'Rounds fired', value: '1' },
            { label: 'Reload time', value: '5.00 (3.25)' },
            { label: 'Energy', value: '88' },
            { label: 'EPS', value: '17.60 (26.20)' },
        ],
        prices: { buy: 214500, sell: 16080, manufacture: 31200 },
        vendor: 16080,
    },
    aegis: {
        id: 'aegis',
        name: 'Aegis Deflection Matrix',
        cat: 'shield',
        slot: 'Shield',
        level: 6,
        glyph: '⬡',
        description:
            'A layered deflection lattice that sheds incoming energy across a phased capacitor bank.',
        buffs: [
            {
                name: 'Shield Inversion',
                scope: 'Equip',
                text: 'Converts 8 (11)% of energy damage to shield recharge.',
            },
            {
                name: 'Reinforced Lattice',
                scope: 'Equip',
                text: '+220 (+290) Shield Capacity when equipped.',
            },
        ],
        stats: [
            { label: 'Shield capacity', value: '1,840 (2,392)' },
            { label: 'Recharge rate', value: '46 (60)/s' },
            { label: 'Energy', value: '64' },
            { label: 'Mass', value: '7.2' },
        ],
        prices: { buy: 132000, sell: 9900, manufacture: 19800 },
        vendor: 9900,
    },
    pulsecore: {
        id: 'pulsecore',
        name: 'Pulsecore Reactor Mk-V',
        cat: 'reactor',
        slot: 'Reactor',
        level: 4,
        glyph: '⊚',
        description:
            'A dependable workhorse reactor. Steady output, easy to maintain in the field.',
        buffs: [{ name: 'Stable Output', scope: 'Equip', text: '+38 (+50) Energy when equipped.' }],
        stats: [
            { label: 'Energy', value: '420 (546)' },
            { label: 'Recharge rate', value: '31 (40)/s' },
            { label: 'Mass', value: '4.8' },
        ],
        prices: { buy: 41200, sell: 3090, manufacture: 6180 },
        vendor: 3090,
    },
    tracer: {
        id: 'tracer',
        name: 'Tracer Sensor Array',
        cat: 'device',
        slot: 'Device',
        level: 5,
        glyph: '◎',
        description:
            'Extends scan resolution and flags cloaked signatures within range. Standard issue for scouts.',
        buffs: [
            {
                name: 'Deep Scan',
                scope: 'Equip',
                text: '+1,200 (+1,560) Scan Range when equipped.',
            },
            {
                name: 'Signature Lock',
                scope: 'Activate',
                text: 'Reveals cloaked contacts for 12 (16)s.',
            },
        ],
        stats: [
            { label: 'Scan range', value: '6,400 (8,320)' },
            { label: 'Energy', value: '54' },
            { label: 'Recharge', value: '90s' },
        ],
        prices: { buy: 98000, sell: 7350, manufacture: 14700 },
        vendor: 7350,
    },
    servo: {
        id: 'servo',
        name: 'Calibrated Servo Assembly',
        cat: 'component',
        slot: 'Component',
        level: 3,
        glyph: '⊞',
        description: 'A precision actuator used in mid-tier weapon and device manufacture.',
        buffs: [],
        stats: [
            { label: 'Tech level', value: '3' },
            { label: 'Mass', value: '0.6' },
            { label: 'Integrity', value: '100%' },
        ],
        prices: { buy: 8400, sell: 630, manufacture: 1260 },
        vendor: 630,
    },
    tungsten: {
        id: 'tungsten',
        name: 'Tungsten Ore',
        cat: 'ore',
        slot: 'Ore',
        level: 1,
        glyph: '◇',
        description:
            'Dense raw tungsten. A staple feedstock for hull and component fabrication. Stacks in lots of 20.',
        buffs: [],
        stats: [
            { label: 'Resource type', value: 'Metal' },
            { label: 'Mass / unit', value: '1.0' },
            { label: 'Stack size', value: '20' },
        ],
        prices: { buy: 320, sell: 24, manufacture: 0 },
        vendor: 24,
    },
    promethium: {
        id: 'promethium',
        name: 'Promethium Ore',
        cat: 'ore',
        slot: 'Ore',
        level: 1,
        glyph: '◇',
        description: 'A faintly radiant ore prized as reactor feedstock. Stacks in lots of 20.',
        buffs: [],
        stats: [
            { label: 'Resource type', value: 'Radioactive' },
            { label: 'Mass / unit', value: '1.2' },
            { label: 'Stack size', value: '20' },
        ],
        prices: { buy: 560, sell: 42, manufacture: 0 },
        vendor: 42,
    },
};

/** Build a fully-resolved Item, rarity derived from this entry's quality. */
function mk(id: string, quality: number | null): Item {
    const base = CATALOG[id];
    return { ...base, rarity: rarityFor(base.level, quality) };
}

export const MOCK_SERVER: ServerStatus = { status: 'ONLINE', players: 1347 };

export const MOCK_SESSION: Session = {
    username: 'kestrel',
    characters: ['Kestrel_Vega', 'Orin_Tasca'],
    credits: 1284960,
    creditsByCharacter: { Kestrel_Vega: 845210, Orin_Tasca: 439750 },
};

export const MOCK_LISTINGS: Listing[] = [
    {
        id: 'L-4471',
        item: mk('claimjumper', 1.68),
        seller: 'Kestrel_Vega',
        stack: 1,
        quality: 1.68,
        band: 'med',
        bid: 559659,
        buyout: 1017562,
        highBidder: 'Orin_Tasca',
    },
    {
        id: 'L-4470',
        item: mk('voidlance', 1.42),
        seller: 'AhBot',
        stack: 1,
        quality: 1.42,
        band: 'high',
        bid: 17688,
        buyout: 32160,
        highBidder: null,
    },
    {
        id: 'L-4468',
        item: mk('aegis', 1.22),
        seller: 'Mara_Sol',
        stack: 1,
        quality: 1.22,
        band: 'low',
        bid: 12870,
        buyout: 19800,
        highBidder: 'Dax_Reyne',
    },
    {
        id: 'L-4465',
        item: mk('tracer', 1.88),
        seller: 'AhBot',
        stack: 1,
        quality: 1.88,
        band: 'high',
        bid: 8085,
        buyout: 14700,
        highBidder: null,
    },
    {
        id: 'L-4463',
        item: mk('pulsecore', 0.96),
        seller: 'Quill_Adaro',
        stack: 1,
        quality: 0.96,
        band: 'med',
        bid: 3399,
        buyout: 6180,
        highBidder: 'AhBot',
    },
    {
        id: 'L-4460',
        item: mk('tungsten', null),
        seller: 'AhBot',
        stack: 20,
        quality: null,
        band: 'high',
        bid: 528,
        buyout: 960,
        highBidder: null,
    },
    {
        id: 'L-4459',
        item: mk('servo', 1.34),
        seller: 'Petra_Kuhn',
        stack: 8,
        quality: 1.34,
        band: 'low',
        bid: 693,
        buyout: 1260,
        highBidder: 'Yuki_Hale',
    },
    {
        id: 'L-4456',
        item: mk('promethium', null),
        seller: 'AhBot',
        stack: 20,
        quality: null,
        band: 'med',
        bid: 924,
        buyout: 1680,
        highBidder: null,
    },
    {
        id: 'L-4454',
        item: mk('voidlance', 1.17),
        seller: 'Dax_Reyne',
        stack: 1,
        quality: 1.17,
        band: 'low',
        bid: 17688,
        buyout: 32160,
        highBidder: null,
    },
    {
        id: 'L-4451',
        item: mk('claimjumper', 1.34),
        seller: 'AhBot',
        stack: 1,
        quality: 1.34,
        band: 'high',
        bid: 43051,
        buyout: 78274,
        highBidder: null,
    },
    {
        id: 'L-4449',
        item: mk('tracer', 1.51),
        seller: 'Sable_Wren',
        stack: 1,
        quality: 1.51,
        band: 'med',
        bid: 8085,
        buyout: 14700,
        highBidder: 'Mara_Sol',
    },
    {
        id: 'L-4446',
        item: mk('servo', null),
        seller: 'AhBot',
        stack: 12,
        quality: null,
        band: 'low',
        bid: 693,
        buyout: 1260,
        highBidder: null,
    },
];

export const MOCK_MAIL: Mail[] = [
    {
        id: 'M-9921',
        subject: 'Auction expired -- item returned',
        sender: 'Auction House',
        recipient: 'Kestrel_Vega',
        read: false,
        expiresInDays: 90,
        body: 'Your auction for the following item did not sell and has been returned to you. A 5% deposit was retained by the Auction House; 95% of your listing deposit is enclosed.\n\nUnclaimed items and credits are removed after 90 days. Please collect your attachments promptly.',
        attachments: [
            { type: 'item', item: mk('aegis', 1.22), stack: 1, quality: 1.22 },
            { type: 'credits', amount: 9405 },
        ],
    },
    {
        id: 'M-9918',
        subject: 'You won: Tracer Sensor Array',
        sender: 'Auction House',
        recipient: 'Kestrel_Vega',
        read: false,
        expiresInDays: 90,
        body: 'Congratulations -- you are the winning bidder. Your item is enclosed. The Auction House thanks you for your patronage.',
        attachments: [{ type: 'item', item: mk('tracer', 1.88), stack: 1, quality: 1.88 }],
    },
    {
        id: 'M-9904',
        subject: 'Sale complete -- credits enclosed',
        sender: 'Auction House',
        recipient: 'Kestrel_Vega',
        read: true,
        expiresInDays: 88,
        body: 'Your listing sold at buyout. Proceeds are enclosed, less the 10% Auction House commission collected at listing.\n\nUnclaimed credits are removed after 90 days.',
        attachments: [{ type: 'credits', amount: 14256 }],
    },
    {
        id: 'M-9890',
        subject: 'Re: That reactor you wanted',
        sender: 'Quill_Adaro',
        recipient: 'Kestrel_Vega',
        read: true,
        expiresInDays: 60,
        body: 'Found a spare Pulsecore in the hold -- listing it on the AH under buyout so you can grab it before anyone else does. Watch the board.\n\n-- Quill',
        attachments: [],
    },
    {
        id: 'M-9885',
        subject: 'Belter Cooperative -- dividend',
        sender: 'Belter Cooperative',
        recipient: 'Kestrel_Vega',
        read: true,
        expiresInDays: 41,
        body: 'Your quarterly mining dividend has been deposited. Fly safe out there, prospector.',
        attachments: [{ type: 'credits', amount: 2500 }],
    },
];

export const MOCK_VAULT: Record<string, VaultSlot[]> = {
    Kestrel_Vega: [
        { slot: 0, item: mk('claimjumper', 1.68), stack: 1, quality: 1.68 },
        { slot: 1, item: mk('tracer', 1.88), stack: 1, quality: 1.88 },
        { slot: 2, item: mk('voidlance', 1.42), stack: 1, quality: 1.42 },
        { slot: 3, item: mk('pulsecore', 0.96), stack: 1, quality: 0.96 },
        { slot: 4, item: mk('servo', 1.34), stack: 9, quality: 1.34 },
        { slot: 5, item: mk('tungsten', null), stack: 20, quality: null },
        { slot: 6, item: mk('promethium', null), stack: 14, quality: null },
    ],
    Orin_Tasca: [
        { slot: 0, item: mk('claimjumper', 1.51), stack: 1, quality: 1.51 },
        { slot: 1, item: mk('voidlance', 1.17), stack: 1, quality: 1.17 },
        { slot: 2, item: mk('servo', null), stack: 12, quality: null },
        { slot: 3, item: mk('tungsten', null), stack: 20, quality: null },
    ],
};

// MOCK_VAULT_STORAGE is each character's ACTUAL vault (the Vault page + mail
// composer source). It mirrors MOCK_VAULT here; the real server includes
// no-trade items in this set (they can be vault-transferred but not mailed/sold).
export const MOCK_VAULT_STORAGE: Record<string, VaultSlot[]> = {
    Kestrel_Vega: [
        { slot: 0, item: mk('claimjumper', 1.68), stack: 1, quality: 1.68 },
        { slot: 1, item: mk('tracer', 1.88), stack: 1, quality: 1.88 },
        { slot: 2, item: mk('voidlance', 1.42), stack: 1, quality: 1.42 },
        { slot: 3, item: mk('pulsecore', 0.96), stack: 1, quality: 0.96 },
        { slot: 4, item: mk('servo', 1.34), stack: 9, quality: 1.34 },
        { slot: 5, item: mk('tungsten', null), stack: 20, quality: null },
        { slot: 6, item: mk('promethium', null), stack: 14, quality: null },
    ],
    Orin_Tasca: [
        { slot: 0, item: mk('claimjumper', 1.51), stack: 1, quality: 1.51 },
        { slot: 1, item: mk('voidlance', 1.17), stack: 1, quality: 1.17 },
        { slot: 2, item: mk('servo', null), stack: 12, quality: null },
        { slot: 3, item: mk('tungsten', null), stack: 20, quality: null },
    ],
};

export const MOCK_MY_LISTINGS: MyListing[] = [
    {
        id: 'S-3012',
        item: mk('claimjumper', 1.68),
        avatar: 'Kestrel_Vega',
        stack: 1,
        quality: 1.68,
        band: 'med',
        currentBid: 559659,
        buyout: 1017562,
    },
    {
        id: 'S-3008',
        item: mk('tracer', 1.51),
        avatar: 'Kestrel_Vega',
        stack: 1,
        quality: 1.51,
        band: 'high',
        currentBid: null,
        buyout: 14700,
    },
    {
        id: 'S-2995',
        item: mk('servo', 1.34),
        avatar: 'Kestrel_Vega',
        stack: 9,
        quality: 1.34,
        band: 'med',
        currentBid: null,
        buyout: 11340,
    },
    {
        id: 'S-2988',
        item: mk('tungsten', null),
        avatar: 'Orin_Tasca',
        stack: 20,
        quality: null,
        band: 'low',
        currentBid: 528,
        buyout: 960,
    },
];

export const DURATIONS = [
    { key: 'short', label: 'Short', hours: 8 },
    { key: 'med', label: 'Medium', hours: 12 },
    { key: 'long', label: 'Long', hours: 24 },
] as const;

// ---- Profile mocks (VITE_MOCK=1 only) ----
export const MOCK_PROFILE: Record<string, AvatarProfile> = {
    Kestrel_Vega: {
        name: 'Kestrel_Vega',
        race: 'Terran',
        class: 'Terran Enforcer',
        classCode: 'TE',
        level: 121,
        combat: { level: 50, bar: 0.62 },
        explore: { level: 41, bar: 0.18 },
        trade: { level: 30, bar: 0.74 },
        sector: 'Vinda',
        credits: 1284500,
    },
    Orin_Tasca: {
        name: 'Orin_Tasca',
        race: 'Jenquai',
        class: 'Jenquai Explorer',
        classCode: 'JE',
        level: 96,
        combat: { level: 22, bar: 0.4 },
        explore: { level: 50, bar: 0.91 },
        trade: { level: 24, bar: 0.12 },
        sector: 'Aganju',
        credits: 642000,
    },
};

export const MOCK_SHIP: Record<string, ShipView> = {
    Kestrel_Vega: {
        name: 'Belter Resolve',
        hullPoints: 4180,
        maxHullPoints: 4400,
        cargoSpace: 78,
        warp: 3000,
        equipment: [
            { slot: 0, quality: 1.68, item: mk('claimjumper', 1.68) },
            { slot: 1, quality: 1.17, item: mk('voidlance', 1.17) },
            { slot: 2, quality: 0.96, item: mk('pulsecore', 0.96) },
        ],
    },
    Orin_Tasca: {
        name: 'Silent Aria',
        hullPoints: 2950,
        maxHullPoints: 2950,
        cargoSpace: 54,
        warp: 2000,
        equipment: [
            { slot: 0, quality: 1.51, item: mk('tracer', 1.51) },
            { slot: 1, quality: 1.34, item: mk('servo', 1.34) },
        ],
    },
};

// A small slice of the galaxy for backend-less design work: a few systems
// across factions, their sectors, and the gate links between them.
export const MOCK_GALAXY: GalaxyMap = {
    systems: [
        { id: 5, name: 'Sol', faction: 'neutral' },
        { id: 6, name: 'Tau Ceti', faction: 'terran' },
        { id: 11, name: 'Vega', faction: 'progen' },
        { id: 1, name: 'Capella', faction: 'jenquai' },
        { id: 7, name: '61 Cygni', faction: 'contested' },
        { id: 10, name: 'Smugglers Run', faction: 'pirate' },
    ],
    sectors: [
        { id: 1060, name: 'Earth', systemId: 5, system: 'Sol', faction: 'neutral' },
        { id: 1020, name: 'High Earth', systemId: 5, system: 'Sol', faction: 'neutral' },
        { id: 1015, name: 'Luna', systemId: 5, system: 'Sol', faction: 'neutral' },
        { id: 1005, name: 'Mercury', systemId: 5, system: 'Sol', faction: 'neutral' },
        { id: 2005, name: 'Hadean', systemId: 6, system: 'Tau Ceti', faction: 'terran' },
        { id: 2010, name: 'Saturn', systemId: 6, system: 'Tau Ceti', faction: 'terran' },
        { id: 3060, name: 'Endriago', systemId: 11, system: 'Vega', faction: 'progen' },
        { id: 3070, name: 'Lagarto', systemId: 11, system: 'Vega', faction: 'progen' },
        { id: 1075, name: 'Antares', systemId: 1, system: 'Capella', faction: 'jenquai' },
        { id: 1076, name: 'Dahin', systemId: 1, system: 'Capella', faction: 'jenquai' },
        { id: 4515, name: 'Paramis', systemId: 7, system: '61 Cygni', faction: 'contested' },
        { id: 4520, name: 'Glenn', systemId: 10, system: 'Smugglers Run', faction: 'pirate' },
    ],
    edges: [
        { from: 1015, to: 1060 },
        { from: 1020, to: 1060 },
        { from: 1005, to: 1015 },
        { from: 1060, to: 2005 },
        { from: 2005, to: 2010 },
        { from: 2010, to: 3060 },
        { from: 3060, to: 3070 },
        { from: 1060, to: 1075 },
        { from: 1075, to: 1076 },
        { from: 2005, to: 4515 },
        { from: 4515, to: 4520 },
    ],
};

export const MOCK_GALAXY_OCCUPANCY: GalaxyOccupancy = {
    counts: { '1060': 50, '2005': 12, '3060': 7, '1075': 25, '4515': 1, '1015': 3 },
    total: 98,
};

export const MOCK_MY_AVATARS: AvatarLocation[] = [
    { name: 'Kestrel_Vega', sector: '1060', online: true },
    { name: 'Orin_Tasca', sector: '2005', online: false },
    { name: 'Mara_Voss', sector: '3060', online: false },
];

export const MOCK_SKILLS: Record<string, SkillView[]> = {
    Kestrel_Vega: [
        { id: 1, name: 'Beam Weapon', category: 'Combat', level: 8, maxLevel: 8 },
        { id: 0, name: 'Afterburn', category: 'Combat', level: 5, maxLevel: 9 },
        { id: 18, name: 'Engine', category: 'Explore', level: 6, maxLevel: 7 },
        { id: 20, name: 'Hull Upgrade', category: 'Explore', level: 4, maxLevel: 9 },
        { id: 45, name: 'Negotiate', category: 'Trade', level: 3, maxLevel: 5 },
        { id: 7, name: 'Build Items', category: 'Total', level: 2, maxLevel: 9 },
    ],
    Orin_Tasca: [
        { id: 2, name: 'Befriend', category: 'Trade', level: 4, maxLevel: 5 },
        { id: 18, name: 'Engine', category: 'Explore', level: 7, maxLevel: 7 },
        { id: 1, name: 'Beam Weapon', category: 'Combat', level: 3, maxLevel: 9 },
    ],
};
