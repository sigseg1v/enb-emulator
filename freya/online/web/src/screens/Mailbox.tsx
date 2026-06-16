// SPDX-License-Identifier: MIT
// Freya Online -- Mailbox (shared per account).
import { useEffect, useMemo, useState } from 'react';
import type { CSSProperties } from 'react';
import type { Attachment, Mail, MailItemInput, VaultSlot } from '../types';
import { MAX_MAIL_ITEMS } from '../types';
import { RARITY } from '../lib/rarity';
import { Credits } from '../components/ui';
import { fmtNum } from '../lib/format';
import * as api from '../api';
import styles from './Mailbox.module.css';

type Vars = CSSProperties & Record<string, string | number>;

function MailRow({ mail, active, onClick }: { mail: Mail; active: boolean; onClick: () => void }) {
    const hasItem = mail.attachments.length > 0;
    return (
        <div className={styles.mail + (active ? ' ' + styles.mailActive : '')} onClick={onClick}>
            <span className={styles.mailUnread + (mail.read ? ' ' + styles.mailUnreadRead : '')} />
            <div className={styles.mailMain}>
                <div className={styles.mailSubj + (mail.read ? '' : ' ' + styles.mailSubjUnread)}>
                    {mail.subject}
                </div>
                <div className={styles.mailFrom}>{mail.sender}</div>
            </div>
            <div className={styles.mailRight}>
                {hasItem && (
                    <span className={styles.mailClip} title="Has item">
                        ◈
                    </span>
                )}
            </div>
        </div>
    );
}

function AttachmentSlot({ att, onLoot }: { att: Attachment; onLoot: () => void }) {
    // Spent squares persist in the list (the server never compacts them); render
    // them dimmed and inert so they can't be re-clicked into an "already looted"
    // error.
    const spent = att.looted === true;
    const spentStyle: CSSProperties = spent ? { opacity: 0.4, pointerEvents: 'none' } : {};
    const handle = spent ? undefined : onLoot;
    const label = spent ? 'Looted' : 'Loot';
    if (att.type === 'credits') {
        return (
            <div className={styles.aslot} style={spentStyle} onClick={handle}>
                <div
                    className={styles.aslotSq}
                    style={
                        {
                            '--rcolor': 'var(--warm)',
                            '--rglow': 'oklch(0.80 0.125 72 / 0.3)',
                        } as Vars
                    }
                >
                    <span className={styles.aslotCr}>cr</span>
                </div>
                <div className={styles.aslotName}>
                    <Credits value={att.amount} />
                </div>
                <div className={styles.aslotLoot}>{label}</div>
            </div>
        );
    }
    const r = RARITY[att.item.rarity];
    return (
        <div className={styles.aslot} style={spentStyle} onClick={handle}>
            <div
                className={styles.aslotSq}
                style={{ '--rcolor': r.color, '--rglow': r.glow } as Vars}
            >
                <span className="icon__glyph">{att.item.glyph}</span>
                {att.stack > 1 && <span className={styles.aslotQty}>{att.stack}</span>}
            </div>
            <div className={styles.aslotName} style={{ color: r.color }}>
                {att.item.name}
            </div>
            <div className={styles.aslotLoot}>{label}</div>
        </div>
    );
}

function lootLabel(att: Attachment): string {
    return att.type === 'credits' ? `${fmtNum(att.amount)} cr` : `${att.item.name} -> vault`;
}

// Compose is the "send mail" form. Sender is the selected topbar character; the
// recipient is any live character name (validated server-side). Items are picked
// from the SENDER's vault (vaultStorage) -- up to MAX_MAIL_ITEMS, and only one
// per slot. Either a non-empty body or at least one item is required. The server
// removes attached items from the vault atomically; we reload after.
function Compose({
    from,
    vaultSlots,
    reload,
    toast,
    onClose,
}: {
    from: string;
    vaultSlots: VaultSlot[];
    reload: () => Promise<void>;
    toast: (msg: string) => void;
    onClose: () => void;
}) {
    const [to, setTo] = useState('');
    const [subject, setSubject] = useState('');
    const [body, setBody] = useState('');
    // Picked vault slots (the attachment set), keyed by slot number.
    const [picked, setPicked] = useState<number[]>([]);
    const [busy, setBusy] = useState(false);

    // Drop any picked slot that no longer exists (e.g. after a reload).
    useEffect(() => {
        setPicked(prev => prev.filter(slot => vaultSlots.some(s => s.slot === slot)));
    }, [vaultSlots]);

    function togglePick(slot: number) {
        setPicked(prev => {
            if (prev.includes(slot)) return prev.filter(s => s !== slot);
            if (prev.length >= MAX_MAIL_ITEMS) {
                toast(`A message can carry at most ${MAX_MAIL_ITEMS} items.`);
                return prev;
            }
            return [...prev, slot];
        });
    }

    const canSend =
        to.trim() !== '' &&
        subject.trim() !== '' &&
        (body.trim() !== '' || picked.length > 0) &&
        !busy;

    async function send() {
        if (!canSend) return;
        const items: MailItemInput[] = picked.map(slot => {
            const s = vaultSlots.find(v => v.slot === slot)!;
            return { slot, itemId: Number(s.item.id) };
        });
        setBusy(true);
        try {
            await api.sendMail({ from, to: to.trim(), subject: subject.trim(), body, items });
            toast(`Mail sent to ${to.trim()}`);
            await reload();
            onClose();
        } catch (err) {
            toast(err instanceof Error ? err.message : 'Send failed');
        } finally {
            setBusy(false);
        }
    }

    return (
        <div className={`${styles.mread} hud-panel hud-corners`}>
            <div className={styles.mreadHead}>
                <button
                    className={`btn btn--sm ${styles.mreadDel}`}
                    disabled={busy}
                    onClick={onClose}
                >
                    Cancel
                </button>
                <h2 className={styles.mreadSubj}>New Message</h2>
                <dl className={styles.mreadMeta}>
                    <dt>From</dt>
                    <dd>{from}</dd>
                </dl>
            </div>

            <div className={styles.compose}>
                <label className={styles.composeField}>
                    <span className={styles.composeLbl}>To</span>
                    <input
                        className={styles.composeInput}
                        placeholder="Recipient character name"
                        value={to}
                        onChange={e => setTo(e.target.value)}
                    />
                </label>
                <label className={styles.composeField}>
                    <span className={styles.composeLbl}>Subject</span>
                    <input
                        className={styles.composeInput}
                        placeholder="Required"
                        maxLength={128}
                        value={subject}
                        onChange={e => setSubject(e.target.value)}
                    />
                </label>
                <label className={styles.composeField}>
                    <span className={styles.composeLbl}>Message</span>
                    <textarea
                        className={`${styles.composeInput} ${styles.composeBody}`}
                        rows={5}
                        maxLength={4000}
                        placeholder="Body (optional if you attach an item)"
                        value={body}
                        onChange={e => setBody(e.target.value)}
                    />
                </label>

                <div className={styles.composeField}>
                    <span className={styles.composeLbl}>
                        Attach from {from}'s vault &middot; {picked.length}/{MAX_MAIL_ITEMS}
                    </span>
                    <div className="vgrid">
                        {vaultSlots.length === 0 ? (
                            <div
                                style={{
                                    gridColumn: '1 / -1',
                                    color: 'var(--text-faint)',
                                    fontFamily: 'var(--font-mono)',
                                    fontSize: 12,
                                    padding: 12,
                                }}
                            >
                                Vault empty
                            </div>
                        ) : (
                            vaultSlots.map(s => {
                                const r = RARITY[s.item.rarity];
                                const on = picked.includes(s.slot);
                                return (
                                    <div
                                        key={s.slot}
                                        className={'vslot' + (on ? ' vslot--sel' : '')}
                                        style={{ '--rcolor': r.color, '--rglow': r.glow } as Vars}
                                        title={`${s.item.name}${s.quality != null ? ` -- Q${s.quality}%` : ''}`}
                                        onClick={() => togglePick(s.slot)}
                                    >
                                        <span className="icon__glyph">{s.item.glyph}</span>
                                        {s.stack > 1 && (
                                            <span className="vslot__qty">{s.stack}</span>
                                        )}
                                        {on && <span className="vslot__pick">✓</span>}
                                    </div>
                                );
                            })
                        )}
                    </div>
                </div>

                <button className="btn btn--primary" disabled={!canSend} onClick={send}>
                    Send Mail
                </button>
            </div>
        </div>
    );
}

export function Mailbox({
    mail,
    setMail,
    character,
    vaultStorage,
    avatars,
    reload,
    toast,
}: {
    mail: Mail[];
    setMail: React.Dispatch<React.SetStateAction<Mail[]>>;
    character: string;
    vaultStorage: Record<string, VaultSlot[]>;
    avatars: string[];
    reload: () => Promise<void>;
    toast: (msg: string) => void;
}) {
    const [activeId, setActiveId] = useState<string | null>(mail[0] ? mail[0].id : null);
    const [composing, setComposing] = useState(false);
    const [busy, setBusy] = useState(false);
    const senderVault = useMemo(() => vaultStorage[character] ?? [], [vaultStorage, character]);

    // Leave compose mode when the active character changes (the sender vault and
    // From line would otherwise be stale).
    useEffect(() => {
        setComposing(false);
    }, [character]);
    void avatars;
    // The inbox is stored per-account, but each message is addressed either to a
    // specific character (m.recipient set) or to the account at large (empty
    // recipient -- e.g. AH credit/winning payouts). Show only mail for the
    // selected character plus account-level mail; switching characters in the
    // topbar re-filters here.
    const visible = mail.filter(m => !m.recipient || m.recipient === character);
    const active = visible.find(m => m.id === activeId) || null;

    function open(m: Mail) {
        setActiveId(m.id);
        if (!m.read) {
            // Optimistic: flip the dot immediately, persist in the background. A failed
            // read-mark is cosmetic, so it only logs -- the next reload reconciles it.
            setMail(prev => prev.map(x => (x.id === m.id ? { ...x, read: true } : x)));
            api.markRead(m.id).catch(err => console.warn('markRead failed', err));
        }
    }

    // Looting moves real credits/items server-side, so we await the API and then
    // reload from the backend rather than mutating local state optimistically.
    // The loot endpoint keys on the server's stable attachment index (att.index),
    // NOT the client array position -- the two only coincide when nothing is
    // looted, because the server keeps spent squares in place. Fall back to the
    // array index for the mock, which carries neither field.
    async function lootIndex(mailId: string, arrIdx: number, att: Attachment) {
        if (busy) return;
        setBusy(true);
        try {
            await api.lootAttachment(mailId, att.index ?? arrIdx);
            toast(`Looted ${lootLabel(att)}`);
            await reload();
        } catch (err) {
            toast(err instanceof Error ? err.message : 'Loot failed');
        } finally {
            setBusy(false);
        }
    }

    async function lootAll(m: Mail) {
        if (busy) return;
        setBusy(true);
        try {
            // Loot every still-unclaimed square by its real server index. The server
            // marks looted in place (no compaction), so indices stay valid as we go
            // and already-looted squares must be skipped -- re-looting one is a 409.
            const pending = m.attachments
                .map((att, i) => ({ att, idx: att.index ?? i }))
                .filter(({ att }) => att.looted !== true);
            for (const { idx } of pending) await api.lootAttachment(m.id, idx);
            const n = pending.length;
            toast(`Looted ${n} attachment${n === 1 ? '' : 's'}`);
            await reload();
        } catch (err) {
            toast(err instanceof Error ? err.message : 'Loot failed');
        } finally {
            setBusy(false);
        }
    }

    async function deleteMail(m: Mail) {
        if (busy) return;
        setBusy(true);
        try {
            await api.deleteMail(m.id);
            setMail(prev => prev.filter(x => x.id !== m.id));
            if (activeId === m.id) setActiveId(null);
            toast('Mail deleted');
        } catch (err) {
            toast(err instanceof Error ? err.message : 'Delete failed');
        } finally {
            setBusy(false);
        }
    }

    const unread = visible.filter(m => !m.read).length;

    return (
        <div className="page">
            <div className="page__head">
                <div>
                    <h1 className="page__title">Mailbox</h1>
                    <div className="page__sub">
                        Account inbox &middot; {character} &middot; {unread} unread
                    </div>
                </div>
                <button
                    className="btn btn--primary"
                    onClick={() => {
                        setComposing(true);
                        setActiveId(null);
                    }}
                >
                    <span style={{ marginRight: 6 }}>✎</span> Compose
                </button>
            </div>

            <div className={styles.mbx}>
                <div className={`${styles.mbxList} hud-panel`}>
                    <div className={styles.mbxListhead}>
                        <span>{visible.length} Messages</span>
                        <span>Subject / Sender</span>
                    </div>
                    <div className={styles.mbxScroll}>
                        {visible.length === 0 ? (
                            <div className="empty">
                                <div className="empty__inner">
                                    <div className="empty__glyph">✉</div>Inbox empty
                                </div>
                            </div>
                        ) : (
                            visible.map(m => (
                                <MailRow
                                    key={m.id}
                                    mail={m}
                                    active={m.id === activeId}
                                    onClick={() => open(m)}
                                />
                            ))
                        )}
                    </div>
                </div>

                {composing ? (
                    <Compose
                        from={character}
                        vaultSlots={senderVault}
                        reload={reload}
                        toast={toast}
                        onClose={() => setComposing(false)}
                    />
                ) : (
                    <div className={`${styles.mread} hud-panel hud-corners`}>
                        {!active ? (
                            <div className="empty">
                                <div className="empty__inner">
                                    <div className="empty__glyph">✉</div>Select a message
                                </div>
                            </div>
                        ) : (
                            <>
                                <div className={styles.mreadHead}>
                                    <button
                                        className={`btn btn--sm ${styles.mreadDel}`}
                                        disabled={busy}
                                        title="Delete this message"
                                        onClick={() => deleteMail(active)}
                                    >
                                        Delete
                                    </button>
                                    <h2 className={styles.mreadSubj}>{active.subject}</h2>
                                    <dl className={styles.mreadMeta}>
                                        <dt>From</dt>
                                        <dd>{active.sender}</dd>
                                        <dt>To</dt>
                                        <dd>{active.recipient}</dd>
                                        <dt>Expires</dt>
                                        <dd>{active.expiresInDays} days</dd>
                                    </dl>
                                </div>

                                {active.attachments.length > 0 && (
                                    <div className={styles.mreadWarn}>
                                        <span>⚠</span>
                                        <span>
                                            This message contains attachments. Unclaimed items and
                                            credits are <b>permanently removed after 90 days</b>.
                                            Collect them to your account before they expire.
                                        </span>
                                    </div>
                                )}

                                <div className={styles.mreadBody}>{active.body}</div>

                                {active.attachments.length > 0 && (
                                    <div className={styles.attach}>
                                        <h3 className={styles.attachTitle}>
                                            Attachments -- {active.attachments.length}
                                        </h3>
                                        <div className={styles.attachGrid}>
                                            {active.attachments.map((att, i) => (
                                                <AttachmentSlot
                                                    key={att.index ?? i}
                                                    att={att}
                                                    onLoot={() => lootIndex(active.id, i, att)}
                                                />
                                            ))}
                                        </div>
                                        {active.attachments.length > 1 && (
                                            <button
                                                className={`btn btn--sm ${styles.attachAll}`}
                                                disabled={busy}
                                                onClick={() => lootAll(active)}
                                            >
                                                Loot All
                                            </button>
                                        )}
                                    </div>
                                )}
                            </>
                        )}
                    </div>
                )}
            </div>
        </div>
    );
}
