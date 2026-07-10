// SPDX-License-Identifier: MIT
// Freya Online -- API client.
//
// All data crosses the wire as JSON over fetch() with the session cookie
// (credentials: 'include'); the Go service sets an HttpOnly cookie via its
// session middleware -- the client never reads or writes the auth token itself.
//
// The Go backend (Phase AQ-1/AQ-2/AQ-3) is live, so these calls hit the real
// JSON API by default and the SPA shows real data. The standalone mock
// (src/mock.ts) is now opt-in for design work -- set VITE_MOCK=1 to render
// without a backend.

import type {
    AvatarLocation,
    AvatarProfile,
    GalaxyGateFlow,
    GalaxyMap,
    GalaxyOccupancy,
    Listing,
    Mail,
    MyListing,
    PostListingInput,
    SearchResult,
    SendMailInput,
    ServerStatus,
    Session,
    ShipView,
    SkillView,
    VaultSlot,
    VaultTransferInput,
} from './types';
import {
    MOCK_GALAXY,
    MOCK_GALAXY_OCCUPANCY,
    MOCK_MY_AVATARS,
    MOCK_LISTINGS,
    MOCK_MAIL,
    MOCK_MY_LISTINGS,
    MOCK_SEARCH,
    MOCK_PROFILE,
    MOCK_SERVER,
    MOCK_SESSION,
    MOCK_SHIP,
    MOCK_SKILLS,
    MOCK_VAULT,
    MOCK_VAULT_STORAGE,
} from './mock';

const USE_MOCK = import.meta.env.VITE_MOCK === '1';

export interface Bootstrap {
    session: Session;
    server: ServerStatus;
    mail: Mail[];
    listings: Listing[];
    vault: Record<string, VaultSlot[]>;
    // vaultStorage is each character's ACTUAL vault (every item, including
    // no-trade), keyed by character name. The Vault page and the mail composer
    // pick items from here; `vault` above is the AH-sellable union (tradable only)
    // used by the Sell grid.
    vaultStorage: Record<string, VaultSlot[]>;
    myListings: MyListing[];
}

// The Go service returns `{ "error": "..." }` with a meaningful status on every
// failure path (e.g. 409 "character is online ...", 402 "insufficient
// credits"). Surface that message verbatim so the UI toast tells the user what
// actually went wrong, falling back to the status code only if the body has no
// error field.
async function errorFor(res: Response, path: string): Promise<Error> {
    try {
        const data = await res.json();
        if (data && typeof data.error === 'string') return new Error(data.error);
    } catch {
        /* non-JSON body -- fall through to the status line */
    }
    return new Error(`${path}: ${res.status}`);
}

async function getJSON<T>(path: string): Promise<T> {
    const res = await fetch(path, { credentials: 'include' });
    if (!res.ok) throw await errorFor(res, path);
    return res.json() as Promise<T>;
}

async function postJSON<T>(path: string, body: unknown): Promise<T> {
    const res = await fetch(path, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
    });
    if (!res.ok) throw await errorFor(res, path);
    return res.json() as Promise<T>;
}

async function delJSON<T>(path: string): Promise<T> {
    const res = await fetch(path, { method: 'DELETE', credentials: 'include' });
    if (!res.ok) throw await errorFor(res, path);
    return res.json() as Promise<T>;
}

/** Always-available, unauthenticated server status (cached 60s server-side). */
export async function fetchStatus(): Promise<ServerStatus> {
    if (USE_MOCK) return structuredClone(MOCK_SERVER);
    return getJSON<ServerStatus>('/api/status');
}

/** Establish a session. Returns the session on success; throws on bad creds. */
export async function login(username: string, password: string): Promise<Session> {
    if (USE_MOCK) {
        if (!username.trim() || !password) throw new Error('Enter your username and password.');
        return { ...structuredClone(MOCK_SESSION), username: username.trim() };
    }
    return postJSON<Session>('/api/login', { username, password });
}

export async function logout(): Promise<void> {
    if (USE_MOCK) return;
    await postJSON<{ ok: boolean }>('/api/logout', {});
}

/** Load everything the authenticated shell needs in one round trip. */
export async function bootstrap(): Promise<Bootstrap> {
    if (USE_MOCK) {
        return {
            session: structuredClone(MOCK_SESSION),
            server: structuredClone(MOCK_SERVER),
            mail: structuredClone(MOCK_MAIL),
            listings: structuredClone(MOCK_LISTINGS),
            vault: structuredClone(MOCK_VAULT),
            vaultStorage: structuredClone(MOCK_VAULT_STORAGE),
            myListings: structuredClone(MOCK_MY_LISTINGS),
        };
    }
    return getJSON<Bootstrap>('/api/bootstrap');
}

// The Profile page loads its three slices on demand for the SELECTED character,
// so switching avatars in the dropdown only re-pulls that character's data --
// not the whole account. Each is a small, indexed, ownership-scoped read.
const enc = encodeURIComponent;

export async function fetchProfile(name: string): Promise<AvatarProfile> {
    if (USE_MOCK) return structuredClone(MOCK_PROFILE[name] ?? MOCK_PROFILE.Kestrel_Vega);
    return getJSON<AvatarProfile>(`/api/avatar/${enc(name)}/profile`);
}

export async function fetchShip(name: string): Promise<ShipView> {
    if (USE_MOCK) return structuredClone(MOCK_SHIP[name] ?? MOCK_SHIP.Kestrel_Vega);
    return getJSON<ShipView>(`/api/avatar/${enc(name)}/ship`);
}

export async function fetchSkills(name: string): Promise<SkillView[]> {
    if (USE_MOCK) return structuredClone(MOCK_SKILLS[name] ?? MOCK_SKILLS.Kestrel_Vega);
    return getJSON<SkillView[]>(`/api/avatar/${enc(name)}/skills`);
}

// The galaxy map: the topology is fetched once (cached hard server-side) and the
// occupancy is polled on a short cadence to drive the per-sector glow.
export async function fetchGalaxy(): Promise<GalaxyMap> {
    if (USE_MOCK) return structuredClone(MOCK_GALAXY);
    return getJSON<GalaxyMap>('/api/galaxy');
}

export async function fetchGalaxyOccupancy(): Promise<GalaxyOccupancy> {
    if (USE_MOCK) return structuredClone(MOCK_GALAXY_OCCUPANCY);
    return getJSON<GalaxyOccupancy>('/api/galaxy/occupancy');
}

// Live directed sector-to-sector traffic over the trailing window. Cached 60s
// server-side; the SPA polls it to animate a travelling light per event along
// each lane. Mock mode has no traffic to show.
export async function fetchGalaxyGateFlow(): Promise<GalaxyGateFlow> {
    if (USE_MOCK) return { lanes: [] };
    return getJSON<GalaxyGateFlow>('/api/galaxy/gateflow');
}

// The logged-in account's own characters and the sectors they sit in (personal
// star markers). Polled alongside occupancy; shown online or not.
export async function fetchMyAvatars(): Promise<AvatarLocation[]> {
    if (USE_MOCK) return structuredClone(MOCK_MY_AVATARS);
    const r = await getJSON<{ avatars: AvatarLocation[] | null }>('/api/galaxy/me');
    return r.avatars ?? [];
}

// Item search: fuzzy item-name lookup returning each item's level,
// manufacturable flag, and the mobs that drop it (combat level + sector).
// Public, read-only game-catalogue data.
export async function searchItems(query: string): Promise<SearchResult> {
    const q = query.trim();
    if (USE_MOCK) {
        const lc = q.toLowerCase();
        return {
            query: q,
            results: MOCK_SEARCH.filter(i => i.name.toLowerCase().includes(lc)),
        };
    }
    return getJSON<SearchResult>(`/api/search?q=${enc(q)}`);
}

// avatar is the selected topbar character: the wallet to debit. The server
// charges this character, not the account's lowest-slot one (PB-31).
export async function placeBid(listingId: string, avatar: string): Promise<Listing> {
    return postJSON<Listing>(`/api/auction/${listingId}/bid`, { avatar });
}

export async function buyout(listingId: string, avatar: string): Promise<{ ok: boolean }> {
    return postJSON<{ ok: boolean }>(`/api/auction/${listingId}/buyout`, { avatar });
}

export async function postListing(input: PostListingInput): Promise<MyListing> {
    return postJSON<MyListing>('/api/auction', input);
}

export async function markRead(mailId: string): Promise<{ ok: boolean }> {
    return postJSON<{ ok: boolean }>(`/api/mail/${mailId}/read`, {});
}

// avatar is the selected topbar character: the vault the looted item lands in.
// Account-level mail (auction wins, returns) would otherwise always loot into
// the account's lowest-slot character, failing if that vault is full even when
// the acting character has room (PB-70).
export async function lootAttachment(
    mailId: string,
    index: number,
    avatar: string,
): Promise<{ ok: boolean }> {
    return postJSON<{ ok: boolean }>(`/api/mail/${mailId}/loot`, { index, avatar });
}

/** Delete a mail message (and its attachments). Unlooted attachments are forfeited. */
export async function deleteMail(mailId: string): Promise<{ ok: boolean }> {
    return delJSON<{ ok: boolean }>(`/api/mail/${mailId}`);
}

/**
 * Compose and send player-to-player mail. The sender must be one of the
 * account's characters; the recipient may be any live character but not the
 * sender. Items (up to MAX_MAIL_ITEMS) come from the sender's vault and are
 * removed atomically server-side. The account is taken from the session cookie.
 */
export async function sendMail(input: SendMailInput): Promise<{ ok: boolean }> {
    return postJSON<{ ok: boolean }>('/api/mail', input);
}

/**
 * Move a whole vault stack from one of the account's characters to another.
 * Both characters must belong to the account and be offline; the move runs in a
 * serializable transaction server-side so it can never dup or lose the item.
 */
export async function transferVault(input: VaultTransferInput): Promise<{ ok: boolean }> {
    return postJSON<{ ok: boolean }>('/api/vault/transfer', input);
}

/**
 * Change the signed-in account's password. The account is identified by the
 * session cookie server-side -- we never send the username. The current password
 * is re-verified by the server; the new password is hashed into the same
 * Argon2id PHC the game login reads, so it works for both website and game.
 */
export async function resetPassword(
    currentPassword: string,
    newPassword: string,
): Promise<{ ok: boolean }> {
    return postJSON<{ ok: boolean }>('/api/account/password', { currentPassword, newPassword });
}
