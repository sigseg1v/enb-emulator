// SPDX-License-Identifier: MIT
// Freya Online -- Account (password reset).
//
// The signed-in account is identified by the session cookie, so this form never
// asks for (or sends) a username -- only the current password, then the new one
// twice. Min length 8 is enforced here for fast feedback and again server-side,
// which is the authority. On success the new password works for both the website
// and the game client (same Argon2id PHC).
import { useState } from 'react';
import * as api from '../api';
import styles from './Account.module.css';

const MIN_LEN = 8;

export function Account({ username, toast }: { username: string; toast: (msg: string) => void }) {
    const [current, setCurrent] = useState('');
    const [next, setNext] = useState('');
    const [confirm, setConfirm] = useState('');
    const [busy, setBusy] = useState(false);
    const [err, setErr] = useState('');

    async function submit(e: React.FormEvent) {
        e.preventDefault();
        setErr('');
        if (!current) {
            setErr('Enter your current password.');
            return;
        }
        if (next.length < MIN_LEN) {
            setErr(`New password must be at least ${MIN_LEN} characters.`);
            return;
        }
        if (next !== confirm) {
            setErr('New passwords do not match.');
            return;
        }
        if (next === current) {
            setErr('New password must differ from the current one.');
            return;
        }

        setBusy(true);
        try {
            await api.resetPassword(current, next);
            setCurrent('');
            setNext('');
            setConfirm('');
            toast('Password changed');
        } catch (ex) {
            setErr(ex instanceof Error ? ex.message : 'Password change failed.');
        } finally {
            setBusy(false);
        }
    }

    return (
        <div className="page">
            <div className="page__head">
                <div>
                    <h1 className="page__title">Account</h1>
                    <div className="page__sub">Security &middot; {username}</div>
                </div>
            </div>

            <div className={styles.account}>
                <form className={`${styles.accountCard} hud-panel hud-corners`} onSubmit={submit}>
                    <div className={styles.accountCardhead}>
                        <span className={`disp ${styles.accountCardtitle}`}>Reset Password</span>
                    </div>
                    <p className={`${styles.accountNote} mono`}>
                        Signed in as <b>{username}</b>. The new password applies to both the website
                        and the game client.
                    </p>

                    <label className="field">
                        <span className="field__label">Current Password</span>
                        <input
                            className="field__input mono"
                            type="password"
                            value={current}
                            autoComplete="current-password"
                            onChange={e => setCurrent(e.target.value)}
                            placeholder="********"
                        />
                    </label>

                    <label className="field">
                        <span className="field__label">New Password</span>
                        <input
                            className="field__input mono"
                            type="password"
                            value={next}
                            autoComplete="new-password"
                            onChange={e => setNext(e.target.value)}
                            placeholder={`at least ${MIN_LEN} characters`}
                        />
                    </label>

                    <label className="field">
                        <span className="field__label">Confirm New Password</span>
                        <input
                            className="field__input mono"
                            type="password"
                            value={confirm}
                            autoComplete="new-password"
                            onChange={e => setConfirm(e.target.value)}
                            placeholder="********"
                        />
                    </label>

                    {err && <div className={`${styles.accountErr} mono`}>{err}</div>}

                    <button
                        className={`btn btn--primary ${styles.accountSubmit}`}
                        type="submit"
                        disabled={busy}
                    >
                        {busy ? 'Updating...' : 'Reset Password'}
                    </button>
                </form>
            </div>
        </div>
    );
}
