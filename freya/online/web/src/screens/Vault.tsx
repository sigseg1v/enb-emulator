// SPDX-License-Identifier: MIT
// Freya Online -- Vault transfer page.
//
// Move an item from one of your characters' vaults into another's. Two character
// dropdowns pick the source (left) and destination (right) -- they must be
// different. Clicking an item on the left arms a "Transfer ->" (into the right
// vault); clicking on the right arms a "<- Transfer" (into the left vault). The
// server validates ownership, the offline-guard, and dup-safety; the whole move
// runs in one serializable transaction there. We just reload after.

import { useEffect, useMemo, useState } from 'react';
import type { CSSProperties } from 'react';
import type { VaultSlot } from '../types';
import { VAULT_SLOT_COUNT } from '../types';
import { RARITY } from '../lib/rarity';
import { Quality } from '../components/ui';
import * as api from '../api';
import styles from './Vault.module.css';

type Vars = CSSProperties & Record<string, string | number>;
type Side = 'left' | 'right';

function VaultGrid({
    title,
    avatar,
    avatars,
    onPick,
    slots,
    selSlot,
    onSelectAvatar,
    exclude,
}: {
    title: string;
    avatar: string;
    avatars: string[];
    slots: VaultSlot[];
    selSlot: number | null;
    onPick: (slot: number) => void;
    onSelectAvatar: (name: string) => void;
    exclude: string;
}) {
    const free = VAULT_SLOT_COUNT - slots.length;
    return (
        <div className="vault hud-panel">
            <div className="vault__head">
                <span className="vault__title">{title}</span>
                <div className="vsel">
                    <select value={avatar} onChange={e => onSelectAvatar(e.target.value)}>
                        {avatars.map(c => (
                            <option key={c} value={c} disabled={c === exclude}>
                                {c}
                            </option>
                        ))}
                    </select>
                </div>
            </div>
            <div
                className="vault__sub"
                style={{
                    padding: '0 12px 6px',
                    fontFamily: 'var(--font-mono)',
                    fontSize: 11,
                    color: 'var(--text-faint)',
                }}
            >
                {slots.length} / {VAULT_SLOT_COUNT} slots used &middot; {free} free
            </div>
            <div className="vgrid">
                {slots.length === 0 ? (
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
                    slots.map(s => {
                        const r = RARITY[s.item.rarity];
                        return (
                            <div
                                key={s.slot}
                                className={'vslot' + (selSlot === s.slot ? ' vslot--sel' : '')}
                                style={{ '--rcolor': r.color, '--rglow': r.glow } as Vars}
                                title={`${s.item.name}${s.quality != null ? ` -- Q${s.quality}%` : ''}`}
                                onClick={() => onPick(s.slot)}
                            >
                                <span className="icon__glyph">{s.item.glyph}</span>
                                {s.stack > 1 && <span className="vslot__qty">{s.stack}</span>}
                            </div>
                        );
                    })
                )}
            </div>
        </div>
    );
}

export function Vault({
    vaultStorage,
    avatars,
    reload,
    toast,
}: {
    vaultStorage: Record<string, VaultSlot[]>;
    avatars: string[];
    reload: () => Promise<void>;
    toast: (msg: string) => void;
}) {
    // Default the two sides to the first two distinct characters. With only one
    // character there is nothing to transfer between, handled below.
    const [left, setLeft] = useState(avatars[0] ?? '');
    const [right, setRight] = useState(avatars[1] ?? avatars[0] ?? '');
    // The armed selection: which side and which slot. Picking on one side clears
    // any pick on the other (only one item moves at a time).
    const [sel, setSel] = useState<{ side: Side; slot: number } | null>(null);
    const [busy, setBusy] = useState(false);

    // If the character list shrinks (reload after a delete), keep the two sides
    // valid and distinct.
    useEffect(() => {
        if (!avatars.includes(left)) setLeft(avatars[0] ?? '');
    }, [avatars, left]);
    useEffect(() => {
        if (!avatars.includes(right) || right === left) {
            setRight(avatars.find(a => a !== left) ?? '');
        }
    }, [avatars, left, right]);

    // Clear the armed selection whenever either side's character changes.
    useEffect(() => {
        setSel(null);
    }, [left, right]);

    const leftSlots = useMemo(() => vaultStorage[left] ?? [], [vaultStorage, left]);
    const rightSlots = useMemo(() => vaultStorage[right] ?? [], [vaultStorage, right]);

    const sameChar = left === right || right === '';
    const armed = sel
        ? ((sel.side === 'left' ? leftSlots : rightSlots).find(s => s.slot === sel.slot) ?? null)
        : null;

    async function transfer() {
        if (!sel || !armed || busy || sameChar) return;
        const from = sel.side === 'left' ? left : right;
        const to = sel.side === 'left' ? right : left;
        setBusy(true);
        try {
            await api.transferVault({ from, to, slot: sel.slot, itemId: Number(armed.item.id) });
            toast(`Transferred ${armed.item.name} from ${from} to ${to}`);
            setSel(null);
            await reload();
        } catch (err) {
            toast(err instanceof Error ? err.message : 'Transfer failed');
        } finally {
            setBusy(false);
        }
    }

    return (
        <div className="page">
            <div className="page__head">
                <div>
                    <h1 className="page__title">Vault</h1>
                    <div className="page__sub">
                        Move items between two of your characters &middot; {VAULT_SLOT_COUNT} slots
                        per vault &middot; both must be offline
                    </div>
                </div>
            </div>

            {avatars.length < 2 ? (
                <div className="hud-panel empty" style={{ padding: '60px 0' }}>
                    <div className="empty__inner">
                        <div className="empty__glyph">⊟</div>You need at least two characters to
                        transfer items
                    </div>
                </div>
            ) : (
                <div className={styles.vxfer}>
                    <div className={styles.vxferCols}>
                        <VaultGrid
                            title="Source / Destination"
                            avatar={left}
                            avatars={avatars}
                            slots={leftSlots}
                            selSlot={sel?.side === 'left' ? sel.slot : null}
                            onPick={slot => setSel({ side: 'left', slot })}
                            onSelectAvatar={setLeft}
                            exclude={right}
                        />

                        <div className={styles.vxferMid}>
                            {armed ? (
                                <button
                                    className="btn btn--primary"
                                    disabled={busy || sameChar}
                                    onClick={transfer}
                                >
                                    {sel?.side === 'left' ? 'Transfer →' : '← Transfer'}
                                </button>
                            ) : (
                                <div
                                    className={styles.vxferHint}
                                    style={{
                                        color: 'var(--text-faint)',
                                        fontFamily: 'var(--font-mono)',
                                        fontSize: 12,
                                        textAlign: 'center',
                                    }}
                                >
                                    Select an item on either side to move it to the other
                                </div>
                            )}
                            {armed && (
                                <div style={{ marginTop: 12, textAlign: 'center' }}>
                                    <div
                                        style={{
                                            color: RARITY[armed.item.rarity].color,
                                            fontWeight: 600,
                                        }}
                                    >
                                        {armed.item.name}
                                        {armed.stack > 1 ? ` ×${armed.stack}` : ''}
                                    </div>
                                    <div
                                        style={{
                                            fontFamily: 'var(--font-mono)',
                                            fontSize: 11,
                                            color: 'var(--text-dim)',
                                        }}
                                    >
                                        L{armed.item.level} &middot; {armed.item.slot}
                                        {armed.quality != null && (
                                            <>
                                                {' '}
                                                &middot; <Quality value={armed.quality} />
                                            </>
                                        )}
                                    </div>
                                </div>
                            )}
                        </div>

                        <VaultGrid
                            title="Source / Destination"
                            avatar={right}
                            avatars={avatars}
                            slots={rightSlots}
                            selSlot={sel?.side === 'right' ? sel.slot : null}
                            onPick={slot => setSel({ side: 'right', slot })}
                            onSelectAvatar={setRight}
                            exclude={left}
                        />
                    </div>
                </div>
            )}
        </div>
    );
}
