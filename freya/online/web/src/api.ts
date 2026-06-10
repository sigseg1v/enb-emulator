// SPDX-License-Identifier: MIT
// Freya Online -- API client.
//
// All data crosses the wire as JSON over fetch() with the session cookie
// (credentials: 'include'); the Go service sets an HttpOnly cookie via its
// session middleware -- the client never reads or writes the auth token itself.
//
// Until the Go backend is wired (Phase AQ-1/AQ-2/AQ-3), VITE_MOCK defaults on
// and these calls resolve from src/mock.ts so the SPA renders standalone. Set
// VITE_MOCK=0 to talk to the real service.

import type {
  Listing, Mail, MyListing, PostListingInput, ServerStatus, Session, VaultSlot,
} from './types';
import {
  MOCK_LISTINGS, MOCK_MAIL, MOCK_MY_LISTINGS, MOCK_SERVER, MOCK_SESSION, MOCK_VAULT,
} from './mock';

const USE_MOCK = import.meta.env.VITE_MOCK !== '0';

export interface Bootstrap {
  session: Session;
  server: ServerStatus;
  mail: Mail[];
  listings: Listing[];
  vault: Record<string, VaultSlot[]>;
  myListings: MyListing[];
}

async function getJSON<T>(path: string): Promise<T> {
  const res = await fetch(path, { credentials: 'include' });
  if (!res.ok) throw new Error(`${path}: ${res.status}`);
  return res.json() as Promise<T>;
}

async function postJSON<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(path, {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`${path}: ${res.status}`);
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
      myListings: structuredClone(MOCK_MY_LISTINGS),
    };
  }
  return getJSON<Bootstrap>('/api/bootstrap');
}

export async function placeBid(listingId: string): Promise<Listing> {
  return postJSON<Listing>(`/api/auction/${listingId}/bid`, {});
}

export async function buyout(listingId: string): Promise<{ ok: boolean }> {
  return postJSON<{ ok: boolean }>(`/api/auction/${listingId}/buyout`, {});
}

export async function postListing(input: PostListingInput): Promise<MyListing> {
  return postJSON<MyListing>('/api/auction', input);
}

export async function markRead(mailId: string): Promise<{ ok: boolean }> {
  return postJSON<{ ok: boolean }>(`/api/mail/${mailId}/read`, {});
}

export async function lootAttachment(mailId: string, index: number): Promise<{ ok: boolean }> {
  return postJSON<{ ok: boolean }>(`/api/mail/${mailId}/loot`, { index });
}
