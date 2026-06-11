// SPDX-License-Identifier: MIT
// Freya Online -- shared domain types.
//
// These mirror the JSON the Go service returns from /api/*. The server is the
// authority for derived fields (rarity, glyph, time band) -- the client only
// renders. Item objects are embedded in listings / vault slots / attachments
// so the UI never has to resolve a separate catalogue across a network call.

export type RarityKey = 'common' | 'uncommon' | 'rare' | 'epic' | 'legendary';
export type Cat = 'weapon' | 'shield' | 'reactor' | 'device' | 'component' | 'ore';
export type Band = 'low' | 'med' | 'high';

export interface Buff { name: string; scope: string; text: string; }
export interface Ammo { name: string; dmg: string; dps: string; range: string; type: string; }
export interface Stat { label: string; value: string; }
export interface Prices { buy: number; sell: number; manufacture: number; }

export interface Item {
  id: string;
  name: string;
  rarity: RarityKey;
  cat: Cat;
  slot: string;
  level: number;
  glyph: string;
  description: string;
  buffs?: Buff[];
  ammo?: Ammo[];
  stats?: Stat[];
  prices: Prices;
  /** Baseline item value (vendor sell price). Bid increments are 1% of this. */
  vendor: number;
  /** Flagged Not Tradable in the content DB -- cannot be listed on the AH. */
  noTrade?: boolean;
}

// One of the three skill disciplines: the discrete level and the fractional
// progress (0..1) toward the next level, used to fill the discipline bar.
export interface Discipline { level: number; bar: number; }

// The Profile identity card. `level` is the overall level (combat+explore+trade).
export interface AvatarProfile {
  name: string;
  race: string;
  class: string;     // e.g. "Terran Enforcer"
  classCode: string; // e.g. "TE"
  level: number;
  combat: Discipline;
  explore: Discipline;
  trade: Discipline;
  sector: string;
  credits: number;
}

export interface EquippedItem {
  slot: number;
  quality: number | null;
  item: Item;
}

export interface ShipView {
  name: string;
  hullPoints: number;
  maxHullPoints: number;
  cargoSpace: number;
  thrust: number;
  warp: number;
  equipment: EquippedItem[];
}

// One learned skill. The UI renders `level` filled of `maxLevel` total dots.
export interface SkillView {
  id: number;
  name: string;
  category: string; // Combat | Explore | Trade | Total
  level: number;
  maxLevel: number;
}

// Galaxy map (first tab). The topology is static content; the occupancy is the
// live light-up data the SPA polls. Faction is a normalized token the UI colors
// by ('jenquai' | 'progen' | 'terran' | 'pirate' | 'contested' | 'neutral' |
// 'deepspace').
export interface GalaxySystem { id: number; name: string; faction: string; }
export interface GalaxySector {
  id: number;
  name: string;
  systemId: number;
  system: string;
  faction: string;
}
export interface GalaxyEdge { from: number; to: number; }
export interface GalaxyMap {
  systems: GalaxySystem[];
  sectors: GalaxySector[];
  edges: GalaxyEdge[];
}
// counts is keyed by sector id (as a string); total is all online players.
export interface GalaxyOccupancy { counts: Record<string, number>; total: number; }

export interface Listing {
  id: string;
  item: Item;
  seller: string;
  stack: number;
  quality: number | null;
  band: Band;
  bid: number;
  buyout: number | null;
  highBidder: string | null;
}

export interface MyListing {
  id: string;
  item: Item;
  avatar: string;
  stack: number;
  quality: number | null;
  band: Band;
  currentBid: number | null;
  buyout: number | null;
}

export interface VaultSlot {
  slot: number;
  item: Item;
  stack: number;
  quality: number | null;
}

// `index` is the attachment's stable position within the message as the server
// orders it (ORDER BY id); it is what /api/mail/{id}/loot expects, NOT the
// client array position. `looted` marks an already-claimed square -- the server
// keeps looted squares in place (it does not compact), so they keep coming back
// on reload and must render spent and be skipped. Both are optional so the
// standalone mock (which has neither concept) still satisfies the type.
export type Attachment =
  | { type: 'credits'; amount: number; index?: number; looted?: boolean }
  | { type: 'item'; item: Item; stack: number; quality: number | null; index?: number; looted?: boolean };

export interface Mail {
  id: string;
  subject: string;
  sender: string;
  recipient: string;
  read: boolean;
  expiresInDays: number;
  body: string;
  attachments: Attachment[];
}

export interface ServerStatus {
  status: 'ONLINE' | 'OFFLINE';
  players: number;
}

export interface Session {
  username: string;
  characters: string[];
  credits: number;
  // Per-character credit balance, keyed by character name. The top bar shows
  // the selected character's value; `credits` above is the account total.
  creditsByCharacter: Record<string, number>;
}

export interface PostListingInput {
  itemId: string;
  slot: number;
  avatar: string;
  stack: number;
  quality: number | null;
  duration: 'short' | 'med' | 'long';
  minBid: number;
  buyout: number;
  fee: number;
}

// The in-game vault capacity (slots 0..95). Mirrors the server const
// vaultSlotCount in store_mail_write.go -- the two MUST agree: the server
// refuses a transfer / loot into a full vault, and the UI uses this to show
// the destination's free-slot count.
export const VAULT_SLOT_COUNT = 96;

// The most attachments one message can carry (mirrors the server's maxMailItems).
export const MAX_MAIL_ITEMS = 6;

// One item the mail composer / vault transfer wants to move: the vault slot and
// the item id the SPA last saw there (the server cross-checks it under a lock).
export interface MailItemInput {
  slot: number;
  itemId: number;
}

export interface SendMailInput {
  from: string;
  to: string;
  subject: string;
  body: string;
  items: MailItemInput[];
}

export interface VaultTransferInput {
  from: string;
  to: string;
  slot: number;
  itemId: number;
}
