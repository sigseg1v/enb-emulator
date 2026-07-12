// SPDX-License-Identifier: MIT
// Freya Online -- Search screen.
//
// Type an item name, query /api/search, and show each matched item's level,
// whether it is manufacturable, and the mobs that drop it -- each with its
// combat level and the sector (name, not id) it is found in. Read-only
// game-catalogue data; no character context needed.
import { useState } from 'react';
import type { SearchItem } from '../types';
import * as api from '../api';
import styles from './Search.module.css';

export function Search() {
    const [term, setTerm] = useState('');
    const [busy, setBusy] = useState(false);
    const [err, setErr] = useState('');
    // null = no search run yet; [] = searched, no matches.
    const [results, setResults] = useState<SearchItem[] | null>(null);
    const [lastQuery, setLastQuery] = useState('');

    async function submit(e: React.FormEvent) {
        e.preventDefault();
        const q = term.trim();
        if (!q) {
            setErr('Enter an item name to search.');
            return;
        }
        setErr('');
        setBusy(true);
        try {
            const res = await api.searchItems(q);
            setResults(res.results);
            setLastQuery(res.query);
        } catch (ex) {
            setErr(ex instanceof Error ? ex.message : 'Search failed.');
            setResults(null);
        } finally {
            setBusy(false);
        }
    }

    return (
        <div className="page">
            <div className="page__head">
                <div>
                    <h1 className="page__title">Search</h1>
                    <div className="page__sub">Item database &middot; drops &amp; sources</div>
                </div>
            </div>

            <div className={styles.search}>
                <form className={styles.searchForm} onSubmit={submit}>
                    <input
                        className="field__input mono"
                        type="text"
                        value={term}
                        onChange={e => setTerm(e.target.value)}
                        placeholder="Item name, e.g. Alluring Laser Class J"
                        autoFocus
                    />
                    <button className="btn btn--primary" type="submit" disabled={busy}>
                        {busy ? 'Searching...' : 'Search'}
                    </button>
                </form>

                {err && <div className={`${styles.searchErr} mono`}>{err}</div>}

                {results !== null && !err && (
                    <div className={styles.searchMeta}>
                        {results.length === 0
                            ? `No items match "${lastQuery}".`
                            : `${results.length} item${results.length === 1 ? '' : 's'} matching "${lastQuery}"`}
                    </div>
                )}

                <div className={styles.searchResults}>
                    {results?.map(item => (
                        <ItemCard key={item.id} item={item} />
                    ))}
                </div>
            </div>
        </div>
    );
}

function ItemCard({ item }: { item: SearchItem }) {
    return (
        <div className={`${styles.card} hud-panel hud-corners`}>
            <div className={styles.cardHead}>
                <span className={`disp ${styles.cardName}`}>{item.name}</span>
                <span className={styles.cardTags}>
                    <span className={styles.tag}>LVL {item.level}</span>
                    <span
                        className={`${styles.tag} ${item.manufacturable ? styles.tagYes : styles.tagNo}`}
                    >
                        {item.manufacturable ? 'Manufacturable' : 'Not manufacturable'}
                    </span>
                </span>
            </div>

            {item.drops.length === 0 ? (
                <div className={`${styles.cardEmpty} mono`}>No known mob drops.</div>
            ) : (
                <table className={styles.drops}>
                    <thead>
                        <tr>
                            <th>Dropped by</th>
                            <th className={styles.numCol}>Combat Lvl</th>
                            <th>Sector</th>
                        </tr>
                    </thead>
                    <tbody>
                        {item.drops.map((d, i) => (
                            <tr key={`${d.mob}-${d.sector}-${i}`}>
                                <td className="mono">{d.mob}</td>
                                <td className={`mono ${styles.numCol}`}>{d.combatLevel}</td>
                                <td className="mono">
                                    {d.sector || <span className={styles.unknown}>unknown</span>}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            )}
        </div>
    );
}
