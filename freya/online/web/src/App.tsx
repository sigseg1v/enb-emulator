// SPDX-License-Identifier: MIT
// Freya Online -- app root.
import { useEffect, useRef, useState } from 'react';
import type {
  Attachment, Listing, Mail, MyListing, ServerStatus as ServerStatusT, Session, VaultSlot,
} from './types';
import * as api from './api';
import { fmtNum } from './lib/format';
import { Credits, ServerStatus, Wordmark } from './components/ui';
import { Login } from './screens/Login';
import { Mailbox } from './screens/Mailbox';
import { AuctionHouse } from './screens/AuctionHouse';

const STATUS_POLL_MS = 60_000;

export default function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [server, setServer] = useState<ServerStatusT>({ status: 'OFFLINE', players: 0 });
  const [tab, setTab] = useState<'mail' | 'ah'>('mail');
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
  function toast(msg: string) {
    setToastMsg(msg);
    clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToastMsg(null), 2600);
  }

  async function signIn(user: string, pass: string) {
    await api.login(user, pass);
    const boot = await api.bootstrap();
    setSession(boot.session);
    setServer(boot.server);
    setMail(boot.mail);
    setListings(boot.listings);
    setVault(boot.vault);
    setMyListings(boot.myListings);
    setCredits(boot.session.credits);
  }

  async function signOut() {
    await api.logout().catch(() => {});
    setSession(null);
  }

  function loot(att: Attachment) {
    if (att.type === 'credits') {
      setCredits(c => c + att.amount);
      toast(`Looted ${fmtNum(att.amount)} cr`);
    } else {
      toast(`Looted ${att.item.name} -> vault`);
    }
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
        {tab === 'mail'
          ? <Mailbox mail={mail} setMail={setMail} character={character} onLoot={loot} />
          : <AuctionHouse listings={listings} setListings={setListings}
              vault={vault} setVault={setVault} myListings={myListings} setMyListings={setMyListings}
              avatars={session.characters} character={character}
              onCharge={amt => setCredits(c => c - amt)} toast={toast} />}
      </div>

      {toastMsg && <div className="toast">{toastMsg}</div>}
    </div>
  );
}
