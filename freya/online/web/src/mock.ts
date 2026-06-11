// SPDX-License-Identifier: MIT
// Freya Online -- dev mock dataset.
//
// Mirrors the Claude Design prototype data so the SPA renders identically with
// no backend running. The real Go service returns the same JSON shapes (see
// src/types.ts). Per-entry rarity is computed with the shared rarityFor() rule
// rather than hard-coded, so the mock obeys the same spec the server will.

import type {
  Item, Listing, MyListing, VaultSlot, Mail, Session, ServerStatus,
} from './types';
import { rarityFor } from './lib/rarity';

type BaseItem = Omit<Item, 'rarity'>;

const CATALOG: Record<string, BaseItem> = {
  claimjumper: {
    id: 'claimjumper', name: "Claimjumper's Reward Seven",
    cat: 'weapon', slot: 'Missile Launcher', level: 7, glyph: '◈',
    description: "A clear message to those who would claim what belongs to the Belters. The weapon delivers a powerful punch.",
    buffs: [
      { name: 'Equipment Engineering', scope: 'Equip', text: 'Reduces equipment install time by 56 (74)% when equipped.' },
      { name: 'Improved Missile Handling', scope: 'Equip', text: 'Improved Missile Accuracy by 2.70 (3.59) Skill Level(s) when equipped.' },
      { name: 'Recharge Reactor', scope: 'Equip', text: '+5.3 (+7.0) Reactor Recharge when equipped.' },
    ],
    ammo: [
      { name: "Miner's Fury -- Explosive (L7)", dmg: '360 (468.00)', dps: '27.48 (54.96)', range: '5500', type: 'Explosive' },
      { name: "Miner's Fury -- Plasma (L7)", dmg: '360 (468.00)', dps: '27.48 (54.96)', range: '5500', type: 'Plasma' },
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
    id: 'voidlance', name: 'Voidlance Beam Projector',
    cat: 'weapon', slot: 'Beam Weapon', level: 8, glyph: '⊳',
    description: 'A focused coherent-particle lance favored by Jenquai patrol wings. Sustained fire melts hull plating.',
    buffs: [
      { name: 'Beam Specialization', scope: 'Equip', text: '+1.8 (2.4) Beam Skill Level(s) when equipped.' },
      { name: 'Targeting Uplink', scope: 'Equip', text: 'Improved critical chance by 4 (6)% when equipped.' },
    ],
    ammo: [
      { name: 'Coherent Plasma Charge (L8)', dmg: '210 (273.00)', dps: '42.00 (84.00)', range: '4200', type: 'Plasma' },
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
    id: 'aegis', name: 'Aegis Deflection Matrix',
    cat: 'shield', slot: 'Shield', level: 6, glyph: '⬡',
    description: 'A layered deflection lattice that sheds incoming energy across a phased capacitor bank.',
    buffs: [
      { name: 'Shield Inversion', scope: 'Equip', text: 'Converts 8 (11)% of energy damage to shield recharge.' },
      { name: 'Reinforced Lattice', scope: 'Equip', text: '+220 (+290) Shield Capacity when equipped.' },
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
    id: 'pulsecore', name: 'Pulsecore Reactor Mk-V',
    cat: 'reactor', slot: 'Reactor', level: 4, glyph: '⊚',
    description: 'A dependable workhorse reactor. Steady output, easy to maintain in the field.',
    buffs: [
      { name: 'Stable Output', scope: 'Equip', text: '+38 (+50) Energy when equipped.' },
    ],
    stats: [
      { label: 'Energy', value: '420 (546)' },
      { label: 'Recharge rate', value: '31 (40)/s' },
      { label: 'Mass', value: '4.8' },
    ],
    prices: { buy: 41200, sell: 3090, manufacture: 6180 },
    vendor: 3090,
  },
  tracer: {
    id: 'tracer', name: 'Tracer Sensor Array',
    cat: 'device', slot: 'Device', level: 5, glyph: '◎',
    description: 'Extends scan resolution and flags cloaked signatures within range. Standard issue for scouts.',
    buffs: [
      { name: 'Deep Scan', scope: 'Equip', text: '+1,200 (+1,560) Scan Range when equipped.' },
      { name: 'Signature Lock', scope: 'Activate', text: 'Reveals cloaked contacts for 12 (16)s.' },
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
    id: 'servo', name: 'Calibrated Servo Assembly',
    cat: 'component', slot: 'Component', level: 3, glyph: '⊞',
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
    id: 'tungsten', name: 'Tungsten Ore',
    cat: 'ore', slot: 'Ore', level: 1, glyph: '◇',
    description: 'Dense raw tungsten. A staple feedstock for hull and component fabrication. Stacks in lots of 20.',
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
    id: 'promethium', name: 'Promethium Ore',
    cat: 'ore', slot: 'Ore', level: 1, glyph: '◇',
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
  { id: 'L-4471', item: mk('claimjumper', 168), seller: 'Kestrel_Vega', stack: 1,  quality: 168, band: 'med',  bid: 559659, buyout: 1017562, highBidder: 'Orin_Tasca' },
  { id: 'L-4470', item: mk('voidlance', 142),   seller: 'AhBot',        stack: 1,  quality: 142, band: 'high', bid: 17688,  buyout: 32160,   highBidder: null },
  { id: 'L-4468', item: mk('aegis', 122),       seller: 'Mara_Sol',     stack: 1,  quality: 122, band: 'low',  bid: 12870,  buyout: 19800,   highBidder: 'Dax_Reyne' },
  { id: 'L-4465', item: mk('tracer', 188),      seller: 'AhBot',        stack: 1,  quality: 188, band: 'high', bid: 8085,   buyout: 14700,   highBidder: null },
  { id: 'L-4463', item: mk('pulsecore', 96),    seller: 'Quill_Adaro',  stack: 1,  quality: 96,  band: 'med',  bid: 3399,   buyout: 6180,    highBidder: 'AhBot' },
  { id: 'L-4460', item: mk('tungsten', null),   seller: 'AhBot',        stack: 20, quality: null, band: 'high', bid: 528,    buyout: 960,     highBidder: null },
  { id: 'L-4459', item: mk('servo', 134),       seller: 'Petra_Kuhn',   stack: 8,  quality: 134, band: 'low',  bid: 693,    buyout: 1260,    highBidder: 'Yuki_Hale' },
  { id: 'L-4456', item: mk('promethium', null), seller: 'AhBot',        stack: 20, quality: null, band: 'med',  bid: 924,    buyout: 1680,    highBidder: null },
  { id: 'L-4454', item: mk('voidlance', 117),   seller: 'Dax_Reyne',    stack: 1,  quality: 117, band: 'low',  bid: 17688,  buyout: 32160,   highBidder: null },
  { id: 'L-4451', item: mk('claimjumper', 134), seller: 'AhBot',        stack: 1,  quality: 134, band: 'high', bid: 43051,  buyout: 78274,   highBidder: null },
  { id: 'L-4449', item: mk('tracer', 151),      seller: 'Sable_Wren',   stack: 1,  quality: 151, band: 'med',  bid: 8085,   buyout: 14700,   highBidder: 'Mara_Sol' },
  { id: 'L-4446', item: mk('servo', null),      seller: 'AhBot',        stack: 12, quality: null, band: 'low',  bid: 693,    buyout: 1260,    highBidder: null },
];

export const MOCK_MAIL: Mail[] = [
  {
    id: 'M-9921', subject: 'Auction expired -- item returned', sender: 'Auction House', recipient: 'Kestrel_Vega',
    read: false, expiresInDays: 90,
    body: "Your auction for the following item did not sell and has been returned to you. A 5% deposit was retained by the Auction House; 95% of your listing deposit is enclosed.\n\nUnclaimed items and credits are removed after 90 days. Please collect your attachments promptly.",
    attachments: [
      { type: 'item', item: mk('aegis', 122), stack: 1, quality: 122 },
      { type: 'credits', amount: 9405 },
    ],
  },
  {
    id: 'M-9918', subject: 'You won: Tracer Sensor Array', sender: 'Auction House', recipient: 'Kestrel_Vega',
    read: false, expiresInDays: 90,
    body: "Congratulations -- you are the winning bidder. Your item is enclosed. The Auction House thanks you for your patronage.",
    attachments: [
      { type: 'item', item: mk('tracer', 188), stack: 1, quality: 188 },
    ],
  },
  {
    id: 'M-9904', subject: 'Sale complete -- credits enclosed', sender: 'Auction House', recipient: 'Kestrel_Vega',
    read: true, expiresInDays: 88,
    body: "Your listing sold at buyout. Proceeds are enclosed, less the 10% Auction House commission collected at listing.\n\nUnclaimed credits are removed after 90 days.",
    attachments: [
      { type: 'credits', amount: 14256 },
    ],
  },
  {
    id: 'M-9890', subject: 'Re: That reactor you wanted', sender: 'Quill_Adaro', recipient: 'Kestrel_Vega',
    read: true, expiresInDays: 60,
    body: "Found a spare Pulsecore in the hold -- listing it on the AH under buyout so you can grab it before anyone else does. Watch the board.\n\n-- Quill",
    attachments: [],
  },
  {
    id: 'M-9885', subject: 'Belter Cooperative -- dividend', sender: 'Belter Cooperative', recipient: 'Kestrel_Vega',
    read: true, expiresInDays: 41,
    body: "Your quarterly mining dividend has been deposited. Fly safe out there, prospector.",
    attachments: [
      { type: 'credits', amount: 2500 },
    ],
  },
];

export const MOCK_VAULT: Record<string, VaultSlot[]> = {
  Kestrel_Vega: [
    { slot: 0, item: mk('claimjumper', 168), stack: 1,  quality: 168 },
    { slot: 1, item: mk('tracer', 188),      stack: 1,  quality: 188 },
    { slot: 2, item: mk('voidlance', 142),   stack: 1,  quality: 142 },
    { slot: 3, item: mk('pulsecore', 96),    stack: 1,  quality: 96 },
    { slot: 4, item: mk('servo', 134),       stack: 9,  quality: 134 },
    { slot: 5, item: mk('tungsten', null),   stack: 20, quality: null },
    { slot: 6, item: mk('promethium', null), stack: 14, quality: null },
  ],
  Orin_Tasca: [
    { slot: 0, item: mk('claimjumper', 151), stack: 1,  quality: 151 },
    { slot: 1, item: mk('voidlance', 117),   stack: 1,  quality: 117 },
    { slot: 2, item: mk('servo', null),      stack: 12, quality: null },
    { slot: 3, item: mk('tungsten', null),   stack: 20, quality: null },
  ],
};

// MOCK_VAULT_STORAGE is each character's ACTUAL vault (the Vault page + mail
// composer source). It mirrors MOCK_VAULT here; the real server includes
// no-trade items in this set (they can be vault-transferred but not mailed/sold).
export const MOCK_VAULT_STORAGE: Record<string, VaultSlot[]> = {
  Kestrel_Vega: [
    { slot: 0, item: mk('claimjumper', 168), stack: 1,  quality: 168 },
    { slot: 1, item: mk('tracer', 188),      stack: 1,  quality: 188 },
    { slot: 2, item: mk('voidlance', 142),   stack: 1,  quality: 142 },
    { slot: 3, item: mk('pulsecore', 96),    stack: 1,  quality: 96 },
    { slot: 4, item: mk('servo', 134),       stack: 9,  quality: 134 },
    { slot: 5, item: mk('tungsten', null),   stack: 20, quality: null },
    { slot: 6, item: mk('promethium', null), stack: 14, quality: null },
  ],
  Orin_Tasca: [
    { slot: 0, item: mk('claimjumper', 151), stack: 1,  quality: 151 },
    { slot: 1, item: mk('voidlance', 117),   stack: 1,  quality: 117 },
    { slot: 2, item: mk('servo', null),      stack: 12, quality: null },
    { slot: 3, item: mk('tungsten', null),   stack: 20, quality: null },
  ],
};

export const MOCK_MY_LISTINGS: MyListing[] = [
  { id: 'S-3012', item: mk('claimjumper', 168), avatar: 'Kestrel_Vega', stack: 1,  quality: 168, band: 'med',  currentBid: 559659, buyout: 1017562 },
  { id: 'S-3008', item: mk('tracer', 151),      avatar: 'Kestrel_Vega', stack: 1,  quality: 151, band: 'high', currentBid: null,   buyout: 14700 },
  { id: 'S-2995', item: mk('servo', 134),       avatar: 'Kestrel_Vega', stack: 9,  quality: 134, band: 'med',  currentBid: null,   buyout: 11340 },
  { id: 'S-2988', item: mk('tungsten', null),   avatar: 'Orin_Tasca',   stack: 20, quality: null, band: 'low',  currentBid: 528,    buyout: 960 },
];

export const DURATIONS = [
  { key: 'short', label: 'Short',  hours: 8 },
  { key: 'med',   label: 'Medium', hours: 12 },
  { key: 'long',  label: 'Long',   hours: 24 },
] as const;
