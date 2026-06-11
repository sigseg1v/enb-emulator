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
