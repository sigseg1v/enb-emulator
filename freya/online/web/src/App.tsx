// SPDX-License-Identifier: MIT
// Freya Online -- app root.
import { useCallback, useEffect, useRef, useState } from 'react';
import type {
  Listing, Mail, MyListing, ServerStatus as ServerStatusT, Session, VaultSlot,
} from './types';
import * as api from './api';
import { Credits, ServerStatus, Wordmark } from './components/ui';
import { Login } from './screens/Login';
import { Mailbox } from './screens/Mailbox';
import { AuctionHouse } from './screens/AuctionHouse';
import { Account } from './screens/Account';

const STATUS_POLL_MS = 60_000;

export default function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [server, setServer] = useState<ServerStatusT>({ status: 'OFFLINE', players: 0 });
  const [tab, setTab] = useState<'mail' | 'ah' | 'account'>('mail');
  const [mail, setMail] = useState<Mail[]>([]);
  const [listings, setListings] = useState<Listing[]>([]);
  const [vault, setVault] = useState<Record<string, VaultSlot[]>>({});
  const [myListings, setMyListings] = useState<MyListing[]>([]);
  const [credits, setCredits] = useState(0);
  const [toastMsg, setToastMsg] = useState<string | null>(null);

  // Server status is always visible (login screen + shell) and refreshed on a
  // 60s cadence. The endpoint itself is cached server-side for 60s.
  useEffect(() => {
    let alive = true;
    const tick = () => api.fetchStatus().then(s => { if (alive) setServer(s); }).catch(() => {});
    tick();
    const h = setInterval(tick, STATUS_POLL_MS);
    return () => { alive = false; clearInterval(h); };
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
    setMyListings(boot.myListings);
    setCredits(boot.session.credits);
  }, []);

  async function signIn(user: string, pass: string) {
    await api.login(user, pass);
    await reload();
  }

  async function signOut() {
    await api.logout().catch(() => {});
    setSession(null);
  }

  if (!session) return <Login server={server} onSignIn={signIn} />;

  const character = session.characters[0];
  const unread = mail.filter(m => !m.read).length;

  return (
    <div className="shell">
      <header className="topbar">
        <Wordmark size={20} />
        <nav className="tabs">
          <button className={'tab' + (tab === 'mail' ? ' tab--active' : '')} onClick={() => setTab('mail')}>
            <span className="tab__glyph">✉</span> Mailbox
            {unread > 0 && <span className="tab__badge">{unread}</span>}
          </button>
          <button className={'tab' + (tab === 'ah' ? ' tab--active' : '')} onClick={() => setTab('ah')}>
            <span className="tab__glyph">◈</span> Auction House
          </button>
          <button className={'tab' + (tab === 'account' ? ' tab--active' : '')} onClick={() => setTab('account')}>
            <span className="tab__glyph">⚙</span> Account
          </button>
        </nav>

        <span className="topbar__spacer" />

        <ServerStatus status={server.status} players={server.players} />
        <span className="topbar__sep" />
        <div className="topbar__acct">
          <span className="topbar__credits"><Credits value={credits} /></span>
          <span className="topbar__sep" />
          <div className="topbar__user">
            <b>{character}</b>
            <span>acct: {session.username}</span>
          </div>
        </div>
        <button className="topbar__logout" title="Sign out" onClick={signOut}>⏻</button>
      </header>

      <div className="shell__body">
        <div className="shell__bg" />
        {tab === 'mail' && (
          <Mailbox mail={mail} setMail={setMail} character={character} reload={reload} toast={toast} />
        )}
        {tab === 'ah' && (
          <AuctionHouse listings={listings} vault={vault} myListings={myListings}
            avatars={session.characters} reload={reload} toast={toast} />
        )}
        {tab === 'account' && (
          <Account username={session.username} toast={toast} />
        )}
      </div>

      {toastMsg && <div className="toast">{toastMsg}</div>}
    </div>
  );
}
