// SPDX-License-Identifier: MIT
// Freya Online -- app root.
import { useCallback, useEffect, useRef, useState } from 'react';
import type {
    Listing,
    Mail,
    MyListing,
    ServerStatus as ServerStatusT,
    Session,
    VaultSlot,
} from './types';
import * as api from './api';
import { Credits, ServerStatus, Wordmark } from './components/ui';
import { Login } from './screens/Login';
import { Galaxy } from './screens/Galaxy';
import { Profile } from './screens/Profile';
import { Mailbox } from './screens/Mailbox';
import { AuctionHouse } from './screens/AuctionHouse';
import { Vault } from './screens/Vault';
import { Account } from './screens/Account';

const STATUS_POLL_MS = 60_000;
// While signed in, re-pull the full bootstrap on this cadence so the server
// status, player count, and the account's live character list stay current
// without a manual action.
const AUTO_RELOAD_MS = 120_000;

export default function App() {
    const [session, setSession] = useState<Session | null>(null);
    // Selected character (index into session.characters). Drives the topbar
    // name dropdown, the mailbox, and the Auction House vault. -1/out-of-range
    // is clamped at render so a shorter character list after a reload is safe.
    const [charIdx, setCharIdx] = useState(0);
    // True until the first rehydrate attempt resolves. We try api.bootstrap()
    // once on mount: a valid session cookie restores the signed-in shell without
    // a fresh login. While this is in flight we show a splash, not the Login
    // screen, so a logged-in refresh doesn't flash the login form.
    const [booting, setBooting] = useState(true);
    const [server, setServer] = useState<ServerStatusT>({ status: 'OFFLINE', players: 0 });
    const [tab, setTab] = useState<'galaxy' | 'profile' | 'mail' | 'ah' | 'vault' | 'account'>(
        'galaxy',
    );
    const [mail, setMail] = useState<Mail[]>([]);
    const [listings, setListings] = useState<Listing[]>([]);
    const [vault, setVault] = useState<Record<string, VaultSlot[]>>({});
    const [vaultStorage, setVaultStorage] = useState<Record<string, VaultSlot[]>>({});
    const [myListings, setMyListings] = useState<MyListing[]>([]);
    const [toastMsg, setToastMsg] = useState<string | null>(null);

    // Server status is always visible (login screen + shell) and refreshed on a
    // 60s cadence. The endpoint itself is cached server-side for 60s.
    useEffect(() => {
        let alive = true;
        const tick = () =>
            api
                .fetchStatus()
                .then(s => {
                    if (alive) setServer(s);
                })
                .catch(() => {});
        tick();
        const h = setInterval(tick, STATUS_POLL_MS);
        return () => {
            alive = false;
            clearInterval(h);
        };
    }, []);

    const toastTimer = useRef<ReturnType<typeof setTimeout>>();
    const toast = useCallback((msg: string) => {
        setToastMsg(msg);
        clearTimeout(toastTimer.current);
        toastTimer.current = setTimeout(() => setToastMsg(null), 2600);
    }, []);

    // The server is the source of truth: after any mutation (bid, buyout, list,
    // loot) we re-pull the authenticated bootstrap and replace every slice. This
    // keeps credits, the vault, listings and the mailbox exactly in step with the
    // backend instead of guessing the delta client-side.
    const reload = useCallback(async () => {
        const boot = await api.bootstrap();
        setSession(boot.session);
        setServer(boot.server);
        setMail(boot.mail);
        setListings(boot.listings);
        setVault(boot.vault);
        setVaultStorage(boot.vaultStorage);
        setMyListings(boot.myListings);
    }, []);

    // Rehydrate from the session cookie on first load: if it is still valid the
    // bootstrap succeeds and we land directly in the shell; a 401 (or any error)
    // just leaves us on the Login screen. Runs exactly once.
    useEffect(() => {
        let alive = true;
        reload()
            .catch(() => {})
            .finally(() => {
                if (alive) setBooting(false);
            });
        return () => {
            alive = false;
        };
    }, [reload]);

    // Auto-refresh the signed-in shell (status, players, characters, credits,
    // vault, listings, mail) every 2 minutes. Gated on a stable boolean so the
    // interval isn't torn down and recreated every time `reload` swaps `session`.
    const loggedIn = session !== null;
    useEffect(() => {
        if (!loggedIn) return;
        const h = setInterval(() => {
            reload().catch(() => {});
        }, AUTO_RELOAD_MS);
        return () => clearInterval(h);
    }, [loggedIn, reload]);

    async function signIn(user: string, pass: string) {
        await api.login(user, pass);
        setCharIdx(0);
        await reload();
    }

    async function signOut() {
        await api.logout().catch(() => {});
        setSession(null);
        setCharIdx(0);
    }

    if (booting && !session) {
        return (
            <div className="boot-splash">
                <Wordmark size={28} />
            </div>
        );
    }
    if (!session) return <Login server={server} onSignIn={signIn} />;

    const safeIdx = charIdx < session.characters.length ? charIdx : 0;
    const character = session.characters[safeIdx];
    // Credits for the selected character (not the account total).
    const charCredits = session.creditsByCharacter?.[character] ?? 0;
    // Unread badge is per selected character (matching the Mailbox filter): mail
    // addressed to this character plus account-level mail (empty recipient).
    const unread = mail.filter(m => !m.read && (!m.recipient || m.recipient === character)).length;

    return (
        <div className="shell">
            <header className="topbar">
                <Wordmark size={20} />
                <nav className="tabs">
                    <button
                        className={'tab' + (tab === 'galaxy' ? ' tab--active' : '')}
                        onClick={() => setTab('galaxy')}
                    >
                        <span className="tab__glyph">✺</span> Galaxy
                    </button>
                    <button
                        className={'tab' + (tab === 'profile' ? ' tab--active' : '')}
                        onClick={() => setTab('profile')}
                    >
                        <span className="tab__glyph">◎</span> Profile
                    </button>
                    <button
                        className={'tab' + (tab === 'mail' ? ' tab--active' : '')}
                        onClick={() => setTab('mail')}
                    >
                        <span className="tab__glyph">✉</span> Mailbox
                        {unread > 0 && <span className="tab__badge">{unread}</span>}
                    </button>
                    <button
                        className={'tab' + (tab === 'ah' ? ' tab--active' : '')}
                        onClick={() => setTab('ah')}
                    >
                        <span className="tab__glyph">◈</span> Auction House
                    </button>
                    <button
                        className={'tab' + (tab === 'vault' ? ' tab--active' : '')}
                        onClick={() => setTab('vault')}
                    >
                        <span className="tab__glyph">⊟</span> Vault
                    </button>
                    <button
                        className={'tab' + (tab === 'account' ? ' tab--active' : '')}
                        onClick={() => setTab('account')}
                    >
                        <span className="tab__glyph">⚙</span> Account
                    </button>
                </nav>

                <span className="topbar__spacer" />

                <ServerStatus status={server.status} players={server.players} />
                <span className="topbar__sep" />
                <div className="topbar__acct">
                    <span className="topbar__credits">
                        <Credits value={charCredits} />
                    </span>
                    <span className="topbar__sep" />
                    <div className="topbar__user">
                        {session.characters.length > 1 ? (
                            <div className="vsel topbar__charsel">
                                <select
                                    value={safeIdx}
                                    onChange={e => setCharIdx(Number(e.target.value))}
                                >
                                    {session.characters.map((c, i) => (
                                        <option key={c} value={i}>
                                            {c}
                                        </option>
                                    ))}
                                </select>
                            </div>
                        ) : (
                            <b>{character}</b>
                        )}
                        <span>acct: {session.username}</span>
                    </div>
                </div>
                <button className="topbar__logout" title="Sign out" onClick={signOut}>
                    ⏻
                </button>
            </header>

            <div className="shell__body">
                <div className="shell__bg" />
                {tab === 'galaxy' && <Galaxy />}
                {tab === 'profile' && <Profile character={character} />}
                {tab === 'mail' && (
                    <Mailbox
                        mail={mail}
                        setMail={setMail}
                        character={character}
                        vaultStorage={vaultStorage}
                        avatars={session.characters}
                        reload={reload}
                        toast={toast}
                    />
                )}
                {tab === 'ah' && (
                    <AuctionHouse
                        listings={listings}
                        vault={vault}
                        myListings={myListings}
                        avatar={character}
                        avatars={session.characters}
                        reload={reload}
                        toast={toast}
                    />
                )}
                {tab === 'vault' && (
                    <Vault
                        vaultStorage={vaultStorage}
                        avatars={session.characters}
                        reload={reload}
                        toast={toast}
                    />
                )}
                {tab === 'account' && <Account username={session.username} toast={toast} />}
            </div>

            {toastMsg && <div className="toast">{toastMsg}</div>}
        </div>
    );
}
