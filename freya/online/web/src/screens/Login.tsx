// SPDX-License-Identifier: MIT
// Freya Online -- login screen.
import { useMemo, useState } from 'react';
import type { ServerStatus as ServerStatusT } from '../types';
import { ServerStatus, Wordmark } from '../components/ui';

/** Deep-space backdrop: procedural starfield + planet with a warm rim. */
function Starfield({ count = 140 }: { count?: number }) {
  const shadow = useMemo(() => {
    const parts: string[] = [];
    for (let i = 0; i < count; i++) {
      const x = (Math.random() * 2000).toFixed(0);
      const y = (Math.random() * 1300).toFixed(0);
      const a = (0.25 + Math.random() * 0.65).toFixed(2);
      parts.push(`${x}px ${y}px 0 0 oklch(0.95 0.02 230 / ${a})`);
    }
    return parts.join(', ');
  }, [count]);
  return (
    <div className="sky">
      <div className="sky__stars" style={{ boxShadow: shadow }} />
      <div className="sky__stars sky__stars--lg" style={{ boxShadow: shadow }} />
      <div className="sky__neb" />
      <div className="sky__planet" />
      <div className="sky__rim" />
    </div>
  );
}

export function Login({
  server, onSignIn,
}: {
  server: ServerStatusT;
  onSignIn: (user: string, pass: string) => Promise<void>;
}) {
  const [user, setUser] = useState('');
  const [pass, setPass] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState('');

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!user.trim() || !pass) { setErr('Enter your username and password.'); return; }
    setErr(''); setBusy(true);
    try {
      await onSignIn(user.trim(), pass);
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'Sign in failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="login">
      <Starfield />
      <div className="login__topbar">
        <Wordmark size={22} />
        <ServerStatus status={server.status} players={server.players} />
      </div>

      <div className="login__center">
        <div className="login__hero">
          <Wordmark size={68} />
          <p className="login__tag">Navigate the Stars, Control the Economy,<br />Survive the Void</p>
          <div className="login__rule" />
          <p className="login__blurb">
            Sign in to manage your account and access the galactic network.
          </p>
        </div>

        <form className="login__card hud-panel hud-corners" onSubmit={submit}>
          <div className="login__cardhead">
            <span className="disp login__cardtitle">Account Sign In</span>
            <span className={'srv__dot ' + (server.status === 'ONLINE' ? 'srv__dot--on' : 'srv__dot--off')} />
          </div>

          <label className="field">
            <span className="field__label">Username</span>
            <input className="field__input mono" value={user} autoFocus autoComplete="username"
              onChange={e => setUser(e.target.value)} placeholder="account-id" />
          </label>

          <label className="field">
            <span className="field__label">Password</span>
            <input className="field__input mono" type="password" value={pass} autoComplete="current-password"
              onChange={e => setPass(e.target.value)} placeholder="********" />
          </label>

          {err && <div className="login__err mono">{err}</div>}

          <button className="btn btn--primary login__submit" type="submit" disabled={busy}>
            {busy ? 'Establishing link...' : 'Sign In'}
          </button>

          <div className="login__foot mono">
            Secure session &middot; authorized pilots only
          </div>
        </form>
      </div>

      <div className="login__legal mono">&copy; 2002&ndash;2026 Freya Online &middot; enb.sigsegv.land<br />All original assets, art and other items that belong to Electronic Arts remain their sole property with all rights reserved to them allowed by law.</div>
    </div>
  );
}
