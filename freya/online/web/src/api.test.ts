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
        const fetchMock = vi
            .fn()
            .mockResolvedValue(new Response(JSON.stringify(body), { status: 200 }));
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
        const fetchMock = vi
            .fn()
            .mockResolvedValue(new Response(JSON.stringify(session), { status: 200 }));
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

    it('resetPassword POSTs current+new to /api/account/password (no username)', async () => {
        const fetchMock = vi
            .fn()
            .mockResolvedValue(new Response(JSON.stringify({ ok: true }), { status: 200 }));
        vi.stubGlobal('fetch', fetchMock);

        const api = await importApi();
        const got = await api.resetPassword('oldpw123', 'newpw4567');

        const [url, opts] = fetchMock.mock.calls[0];
        expect(url).toBe('/api/account/password');
        expect(opts.method).toBe('POST');
        expect(opts.credentials).toBe('include');
        const sent = JSON.parse(opts.body);
        expect(sent).toEqual({ currentPassword: 'oldpw123', newPassword: 'newpw4567' });
        expect(sent.username).toBeUndefined(); // identity comes from the cookie
        expect(got).toEqual({ ok: true });
    });

    it('resetPassword surfaces the server error message on failure', async () => {
        const fetchMock = vi.fn().mockResolvedValue(
            new Response(JSON.stringify({ error: 'current password is incorrect' }), {
                status: 401,
            }),
        );
        vi.stubGlobal('fetch', fetchMock);

        const api = await importApi();
        await expect(api.resetPassword('wrong', 'newpw4567')).rejects.toThrow(
            'current password is incorrect',
        );
    });

    it('bootstrap GETs /api/bootstrap', async () => {
        const fetchMock = vi.fn().mockResolvedValue(
            new Response(
                JSON.stringify({
                    session: {},
                    server: {},
                    mail: [],
                    listings: [],
                    vault: {},
                    myListings: [],
                }),
                { status: 200 },
            ),
        );
        vi.stubGlobal('fetch', fetchMock);

        const api = await importApi();
        await api.bootstrap();
        expect(fetchMock).toHaveBeenCalledWith('/api/bootstrap', { credentials: 'include' });
    });

    it('fetchGalaxy GETs /api/galaxy (topology)', async () => {
        const body = { systems: [], sectors: [], edges: [] };
        const fetchMock = vi
            .fn()
            .mockResolvedValue(new Response(JSON.stringify(body), { status: 200 }));
        vi.stubGlobal('fetch', fetchMock);

        const api = await importApi();
        const got = await api.fetchGalaxy();
        expect(fetchMock).toHaveBeenCalledWith('/api/galaxy', { credentials: 'include' });
        expect(got).toEqual(body);
    });

    it('fetchGalaxyOccupancy GETs /api/galaxy/occupancy (live counts)', async () => {
        const body = { counts: { '1060': 3 }, total: 3 };
        const fetchMock = vi
            .fn()
            .mockResolvedValue(new Response(JSON.stringify(body), { status: 200 }));
        vi.stubGlobal('fetch', fetchMock);

        const api = await importApi();
        const got = await api.fetchGalaxyOccupancy();
        expect(fetchMock).toHaveBeenCalledWith('/api/galaxy/occupancy', { credentials: 'include' });
        expect(got).toEqual(body);
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
        expect(boot.vaultStorage).toBeTruthy();
    });

    it('sendMail POSTs /api/mail with the composed payload', async () => {
        const fetchMock = vi
            .fn()
            .mockResolvedValue(new Response(JSON.stringify({ ok: true }), { status: 200 }));
        vi.stubGlobal('fetch', fetchMock);

        const api = await importApi();
        await api.sendMail({ from: 'A_One', to: 'B_Two', subject: 'hi', body: 'yo', items: [] });
        expect(fetchMock).toHaveBeenCalledWith(
            '/api/mail',
            expect.objectContaining({ method: 'POST' }),
        );
    });

    it('transferVault POSTs /api/vault/transfer', async () => {
        const fetchMock = vi
            .fn()
            .mockResolvedValue(new Response(JSON.stringify({ ok: true }), { status: 200 }));
        vi.stubGlobal('fetch', fetchMock);

        const api = await importApi();
        await api.transferVault({ from: 'A_One', to: 'B_Two', slot: 0, itemId: 42 });
        expect(fetchMock).toHaveBeenCalledWith(
            '/api/vault/transfer',
            expect.objectContaining({ method: 'POST' }),
        );
    });

    it('serves the galaxy topology + occupancy from the mock without fetch', async () => {
        const fetchMock = vi.fn();
        vi.stubGlobal('fetch', fetchMock);

        const api = await importApi();
        const m = await api.fetchGalaxy();
        const o = await api.fetchGalaxyOccupancy();
        expect(fetchMock).not.toHaveBeenCalled();
        expect(m.sectors.length).toBeGreaterThan(0);
        expect(o.total).toBeGreaterThan(0);
    });
});
