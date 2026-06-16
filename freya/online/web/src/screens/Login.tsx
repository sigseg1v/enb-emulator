// SPDX-License-Identifier: MIT
// Freya Online -- login screen.
import { useMemo, useState } from 'react';
import type { ServerStatus as ServerStatusT } from '../types';
import { ServerStatus, Wordmark } from '../components/ui';
import styles from './Login.module.css';

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
        <div className={styles.sky}>
            <div className={styles.skyStars} style={{ boxShadow: shadow }} />
            <div
                className={`${styles.skyStars} ${styles.skyStarsLg}`}
                style={{ boxShadow: shadow }}
            />
            <div className={styles.skyNeb} />
            <div className={styles.skyPlanet} />
            <div className={styles.skyRim} />
        </div>
    );
}

export function Login({
    server,
    onSignIn,
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
        if (!user.trim() || !pass) {
            setErr('Enter your username and password.');
            return;
        }
        setErr('');
        setBusy(true);
        try {
            await onSignIn(user.trim(), pass);
        } catch (ex) {
            setErr(ex instanceof Error ? ex.message : 'Sign in failed.');
        } finally {
            setBusy(false);
        }
    }

    return (
        <div className={styles.login}>
            <Starfield />
            <div className={styles.loginTopbar}>
                <Wordmark size={22} />
                <ServerStatus status={server.status} players={server.players} />
            </div>

            <div className={styles.loginCenter}>
                <div className={styles.loginHero}>
                    <Wordmark size={68} />
                    <p className={styles.loginTag}>
                        Navigate the Stars, Control the Economy,
                        <br />
                        Survive the Void
                    </p>
                    <div className={styles.loginRule} />
                    <p className={styles.loginBlurb}>
                        Sign in to manage your account and access the galactic network.
                    </p>
                </div>

                <form className={`${styles.loginCard} hud-panel hud-corners`} onSubmit={submit}>
                    <div className={styles.loginCardhead}>
                        <span className={`disp ${styles.loginCardtitle}`}>Account Sign In</span>
                        <span
                            className={
                                'srv__dot ' +
                                (server.status === 'ONLINE' ? 'srv__dot--on' : 'srv__dot--off')
                            }
                        />
                    </div>

                    <label className="field">
                        <span className="field__label">Username</span>
                        <input
                            className="field__input mono"
                            value={user}
                            autoFocus
                            autoComplete="username"
                            onChange={e => setUser(e.target.value)}
                            placeholder="account-id"
                        />
                    </label>

                    <label className="field">
                        <span className="field__label">Password</span>
                        <input
                            className="field__input mono"
                            type="password"
                            value={pass}
                            autoComplete="current-password"
                            onChange={e => setPass(e.target.value)}
                            placeholder="********"
                        />
                    </label>

                    {err && <div className={`${styles.loginErr} mono`}>{err}</div>}

                    <button
                        className={`btn btn--primary ${styles.loginSubmit}`}
                        type="submit"
                        disabled={busy}
                    >
                        {busy ? 'Establishing link...' : 'Sign In'}
                    </button>

                    <div className={`${styles.loginFoot} mono`}>
                        Secure session &middot; authorized pilots only
                    </div>
                </form>
            </div>

            <div className={`${styles.loginLegal} mono`}>
                &copy; 2026 Freya Online &middot; enb.sigsegv.land
                <br />
                All original assets, art and other items that belong to Electronic Arts remain their
                sole property with all rights reserved to them allowed by law.
            </div>
        </div>
    );
}
