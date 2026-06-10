// SPDX-License-Identifier: MIT
// Freya Online -- API client dispatch tests.
//
// The headline behaviour this project just changed: the SPA talks to the REAL
// Go JSON API by default, and the standalone mock is opt-in via VITE_MOCK=1.
// USE_MOCK is a module-load constant, so each case stubs the env and
// re-imports the module fresh (vi.resetModules) before asserting which path it
// takes -- real fetch (with the session cookie) vs in-memory mock (no fetch).

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

async function importApi() {
  vi.resetModules();
  return import('./api');
}

afterEach(() => {
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
});

describe('real backend (default, VITE_MOCK unset)', () => {
  beforeEach(() => {
    vi.stubEnv('VITE_MOCK', ''); // default -> real
  });

  it('fetchStatus GETs /api/status with the session cookie', async () => {
    const body = { online: true, players: 7 };
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(body), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const api = await importApi();
    const status = await api.fetchStatus();

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, opts] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/status');
    expect(opts.credentials).toBe('include');
    expect(status).toEqual(body);
  });

  it('login POSTs JSON credentials and returns the server session', async () => {
    const session = { username: 'alice', characters: [], credits: 0 };
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(session), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const api = await importApi();
    const got = await api.login('alice', 'hunter2');

    const [url, opts] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/login');
    expect(opts.method).toBe('POST');
    expect(opts.credentials).toBe('include');
    expect(JSON.parse(opts.body)).toEqual({ username: 'alice', password: 'hunter2' });
    expect(got).toEqual(session);
  });

  it('throws on a non-2xx response (bad credentials surface to the UI)', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('', { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);

    const api = await importApi();
    await expect(api.login('alice', 'wrong')).rejects.toThrow('401');
  });

  it('bootstrap GETs /api/bootstrap', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ session: {}, server: {}, mail: [], listings: [], vault: {}, myListings: [] }), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const api = await importApi();
    await api.bootstrap();
    expect(fetchMock).toHaveBeenCalledWith('/api/bootstrap', { credentials: 'include' });
  });
});

describe('mock backend (VITE_MOCK=1, opt-in)', () => {
  beforeEach(() => {
    vi.stubEnv('VITE_MOCK', '1');
  });

  it('serves status from the mock without touching fetch', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    const api = await importApi();
    const status = await api.fetchStatus();

    expect(fetchMock).not.toHaveBeenCalled();
    expect(status).toBeTruthy();
  });

  it('login validates input but never hits the network', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    const api = await importApi();
    const s = await api.login('  bob ', 'pw');
    expect(fetchMock).not.toHaveBeenCalled();
    expect(s.username).toBe('bob'); // trimmed

    await expect(api.login('', '')).rejects.toThrow();
  });

  it('bootstrap returns a complete mock payload without fetch', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    const api = await importApi();
    const boot = await api.bootstrap();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(boot.session).toBeTruthy();
    expect(Array.isArray(boot.listings)).toBe(true);
    expect(Array.isArray(boot.mail)).toBe(true);
  });
});
