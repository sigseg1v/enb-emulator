// SPDX-License-Identifier: MIT
// Freya Online -- Auction House (Buy + Sell subtabs).
import { useEffect, useMemo, useState } from 'react';
import type { CSSProperties } from 'react';
import type { Cat, Listing, MyListing, PostListingInput, VaultSlot } from '../types';
import { RARITY } from '../lib/rarity';
import { bidStartOf, buyoutOf, fmtNum, minIncrementOf } from '../lib/format';
import { Credits, ItemIcon, Quality, TimeBand } from '../components/ui';
import { ItemDisplay } from '../components/ItemDisplay';
import { DURATIONS } from '../mock';
import * as api from '../api';
import styles from './AuctionHouse.module.css';

type Vars = CSSProperties & Record<string, string | number>;
type Filter = 'all' | Cat;

const AH_FILTERS: { key: Filter; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'weapon', label: 'Weapons' },
  { key: 'shield', label: 'Shields' },
  { key: 'reactor', label: 'Reactors' },
  { key: 'device', label: 'Devices' },
  { key: 'component', label: 'Components' },
  { key: 'ore', label: 'Ore' },
];

function AhRow({ listing, active, onClick }: { listing: Listing; active: boolean; onClick: () => void }) {
  const r = RARITY[listing.item.rarity];
  return (
    <div className={styles.ahrow + (active ? ' ' + styles.ahrowActive : '')} onClick={onClick}>
      <ItemIcon item={listing.item} className={styles.ahrowIcon} />
      <div style={{ minWidth: 0, '--rcolor': r.color } as Vars}>
        <div className={styles.ahrowName} style={{ color: r.color }}>
          {listing.item.name}{listing.stack > 1 ? ` ×${listing.stack}` : ''}
        </div>
        <div className={styles.ahrowMeta}>L{listing.item.level} &middot; {listing.item.slot}</div>
      </div>
      <div className={styles.ahrowSeller + (listing.seller === 'AhBot' ? ' ' + styles.ahrowSellerBot : '')}>{listing.seller}</div>
      <div className={styles.ahrowNum}>{listing.quality != null ? <Quality value={listing.quality} /> : <span className="dash">-</span>}</div>
      <div className={styles.ahrowNum}><TimeBand band={listing.band} showLabel={false} /></div>
      <div className={styles.ahrowPrice}><b><Credits value={listing.bid} /></b></div>
      <div className={`${styles.ahrowPrice} ${styles.ahrowPriceBuyout}`}>{listing.buyout != null ? <b><Credits value={listing.buyout} /></b> : <span className="dash">-</span>}</div>
    </div>
  );
}

/* ---------------- BUY ---------------- */
function BuyView({
  listings, reload, toast,
}: {
  listings: Listing[];
  reload: () => Promise<void>;
  toast: (msg: string) => void;
}) {
  const [q, setQ] = useState('');
  const [filter, setFilter] = useState<Filter>('all');
  const [activeId, setActiveId] = useState<string | null>(listings[0] ? listings[0].id : null);
  const [busy, setBusy] = useState(false);

  const shown = useMemo(() => listings.filter(l => {
    if (filter !== 'all' && l.item.cat !== filter) return false;
    if (q && !l.item.name.toLowerCase().includes(q.toLowerCase()) && !l.seller.toLowerCase().includes(q.toLowerCase())) return false;
    return true;
  }), [listings, filter, q]);

  const active = listings.find(l => l.id === activeId) || null;
  const minInc = active ? minIncrementOf(active.item) : 0;

  async function placeBid() {
    if (!active || busy) return;
    setBusy(true);
    try {
      await api.placeBid(active.id);
      toast('Bid placed -- you are the high bidder');
      await reload();
    } catch (err) {
      toast(err instanceof Error ? err.message : 'Bid failed');
    } finally {
      setBusy(false);
    }
  }
  async function buyoutNow() {
    if (!active || active.buyout == null || busy) return;
    const price = active.buyout;
    setBusy(true);
    try {
      await api.buyout(active.id);
      toast(`Bought out -- ${fmtNum(price)} cr -- item sent to your mailbox`);
      setActiveId(null);
      await reload();
    } catch (err) {
      toast(err instanceof Error ? err.message : 'Buyout failed');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className={styles.ah}>
      <div className={`${styles.ahLeft} hud-panel`}>
        <div className={styles.ahToolbar}>
          <div className={styles.search}>
            <span className={styles.searchIcon}>⌕</span>
            <input className={styles.searchInput} placeholder="Search item or seller..." value={q} onChange={e => setQ(e.target.value)} />
          </div>
          <div className={styles.chips}>
            {AH_FILTERS.map(f => (
              <button key={f.key} className={styles.chip + (filter === f.key ? ' ' + styles.chipOn : '')} onClick={() => setFilter(f.key)}>{f.label}</button>
            ))}
          </div>
        </div>

        <div className={styles.ahtable}>
          <div className={`${styles.ahrow} ${styles.ahrowHead}`}>
            <div></div><div>Item</div><div>Seller</div>
            <div className={styles.ahrowNum}>Quality</div>
            <div className={styles.ahrowNum}>Time</div>
            <div style={{ textAlign: 'right' }}>Bid</div>
            <div style={{ textAlign: 'right' }}>Buyout</div>
          </div>
          {shown.length === 0
            ? <div className="empty" style={{ padding: '60px 0' }}><div className="empty__inner"><div className="empty__glyph">⌕</div>No listings match</div></div>
            : shown.map(l => <AhRow key={l.id} listing={l} active={l.id === activeId} onClick={() => setActiveId(l.id)} />)}
        </div>
      </div>

      <div className={styles.ahDetail}>
        {!active
          ? <div className="hud-panel empty" style={{ height: '100%' }}><div className="empty__inner"><div className="empty__glyph">◈</div>Select a listing</div></div>
          : (
            <>
              <ItemDisplay item={active.item} quality={active.quality} />
              <div className={`${styles.bidbox} hud-panel hud-corners`}>
                <div className={styles.bidboxRow}>
                  <span>Seller</span>
                  <span className={active.seller === 'AhBot' ? styles.ahrowSellerBot : ''} style={{ fontFamily: 'var(--font-mono)' }}>{active.seller}</span>
                </div>
                <div className={styles.bidboxRow}><span>Time remaining</span><TimeBand band={active.band} /></div>
                <div className={styles.bidboxRow}>
                  <span>High bidder</span>
                  <span className={'mono ' + (active.highBidder ? styles.bidder : styles.bidderNone)}>{active.highBidder || 'No bids yet'}</span>
                </div>
                <div className={styles.bidboxRow}><span>Current bid</span><span className={styles.bidboxBig}><Credits value={active.bid} /></span></div>
                {active.buyout != null &&
                  <div className={styles.bidboxRow}><span>Buyout</span><span className={styles.bidboxBig} style={{ color: 'var(--warm)' }}><Credits value={active.buyout} /></span></div>}
                <p className={styles.bidboxHint}>Next bid must be at least <b style={{ color: 'var(--text-dim)' }}>+{fmtNum(minInc)} cr</b> (1% of item value). Quantity in stack: {active.stack}.</p>
                <div className={styles.bidboxActions}>
                  <button className="btn" onClick={placeBid} disabled={busy}>Bid {fmtNum(active.bid + minInc)}</button>
                  <button className="btn btn--warm" onClick={buyoutNow} disabled={busy || active.buyout == null}>Buyout</button>
                </div>
              </div>
            </>
          )}
      </div>
    </div>
  );
}

/* ---------------- SELL ---------------- */
function MyListingRow({ listing }: { listing: MyListing }) {
  const r = RARITY[listing.item.rarity];
  return (
    <div className={styles.ahrow} style={{ cursor: 'default' }}>
      <ItemIcon item={listing.item} className={styles.ahrowIcon} />
      <div style={{ minWidth: 0 }}>
        <div className={styles.ahrowName} style={{ color: r.color }}>{listing.item.name}{listing.stack > 1 ? ` ×${listing.stack}` : ''}</div>
        <div className={styles.ahrowMeta}>L{listing.item.level} &middot; {listing.item.slot}</div>
      </div>
      <div className={styles.ahrowSeller}>{listing.avatar}</div>
      <div className={styles.ahrowNum}>{listing.quality != null ? <Quality value={listing.quality} /> : <span className="dash">-</span>}</div>
      <div className={styles.ahrowNum}><TimeBand band={listing.band} showLabel={false} /></div>
      <div className={styles.ahrowPrice}>{listing.currentBid != null ? <b><Credits value={listing.currentBid} /></b> : <span className="dash">-</span>}</div>
      <div className={`${styles.ahrowPrice} ${styles.ahrowPriceBuyout}`}>{listing.buyout != null ? <b><Credits value={listing.buyout} /></b> : <span className="dash">-</span>}</div>
    </div>
  );
}

function PostForm({
  slotData, avatar, onPost, busy,
}: {
  slotData: VaultSlot;
  avatar: string;
  onPost: (p: PostListingInput) => void;
  busy: boolean;
}) {
  const item = slotData.item;
  const r = RARITY[item.rarity];
  const maxStack = slotData.stack;
  const [duration, setDuration] = useState<'short' | 'med' | 'long'>('med');
  const [qty, setQty] = useState(1);
  const [minBid, setMinBid] = useState<number | string>(bidStartOf(item));
  const [buyout, setBuyout] = useState<number | string>(buyoutOf(item));

  useEffect(() => {
    setDuration('med'); setQty(1);
    setMinBid(bidStartOf(item));
    setBuyout(buyoutOf(item));
  }, [slotData.slot, item, avatar]);

  const fee = Math.round((Number(minBid) || 0) * 0.1);

  function post() {
    onPost({
      itemId: item.id, slot: slotData.slot, avatar,
      stack: qty, quality: slotData.quality, duration,
      minBid: Number(minBid) || 0, buyout: Number(buyout) || 0, fee,
    });
  }

  return (
    <div className={`${styles.pform} hud-panel hud-corners`}>
      <div className={styles.pformPreview} style={{ '--rcolor': r.color, '--rglow': r.glow } as Vars}>
        <ItemIcon item={item} className={styles.pformIcon} />
        <div>
          <div className={styles.pformPname}>{item.name}</div>
          <div className={styles.pformPsub}>L{item.level} &middot; {item.slot}{slotData.quality != null && <> &middot; Quality <Quality value={slotData.quality} /></>}</div>
        </div>
      </div>

      <div className={styles.pformField}>
        <span className={styles.pformLbl}>Post duration</span>
        <div className={styles.seg}>
          {DURATIONS.map(d => (
            <button key={d.key} className={styles.segBtn + (duration === d.key ? ' ' + styles.segBtnOn : '')} onClick={() => setDuration(d.key)}>
              {d.label}<small>{d.hours}h</small>
            </button>
          ))}
        </div>
      </div>

      <div className={styles.pformField}>
        <span className={styles.pformLbl}>Stack size</span>
        <div className={styles.stepper}>
          <button className={styles.stepperBtn} disabled={qty <= 1} onClick={() => setQty(v => Math.max(1, v - 1))}>−</button>
          <div className={styles.stepperVal}>{qty}</div>
          <button className={styles.stepperBtn} disabled={qty >= maxStack} onClick={() => setQty(v => Math.min(maxStack, v + 1))}>+</button>
        </div>
        <div className={styles.stepperMax}>{maxStack === 1 ? 'Single item -- fixed at 1' : `Up to ${maxStack} available in this slot`}</div>
      </div>

      <div className={styles.pformField}>
        <span className={styles.pformLbl}>Minimum bid</span>
        <div className={styles.pinput}>
          <input type="number" min="1" value={minBid} onChange={e => setMinBid(e.target.value)} />
          <span className={styles.pinputUnit}>cr</span>
        </div>
      </div>

      <div className={styles.pformField}>
        <span className={styles.pformLbl}>Buyout price <span style={{ textTransform: 'none', letterSpacing: 0 }}>(optional)</span></span>
        <div className={styles.pinput}>
          <input type="number" min="0" value={buyout} onChange={e => setBuyout(e.target.value)} />
          <span className={styles.pinputUnit}>cr</span>
        </div>
      </div>

      <p className={styles.pformFee}>Auction House fee <b>10%</b> &middot; <b>{fmtNum(fee)} cr</b> collected now. Unsold items return 95% to your mailbox.</p>
      <button className={`btn btn--primary ${styles.pformPost}`} onClick={post} disabled={busy}>List on Auction House</button>
    </div>
  );
}

function SellView({
  vault, myListings, avatar, avatars, reload, toast,
}: {
  vault: Record<string, VaultSlot[]>;
  myListings: MyListing[];
  avatar: string;
  avatars: string[];
  reload: () => Promise<void>;
  toast: (msg: string) => void;
}) {
  const [selSlot, setSelSlot] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);

  // The active avatar is chosen from the top-bar character dropdown; clear any
  // selected vault slot when it changes so we never carry a stale selection.
  useEffect(() => { setSelSlot(null); }, [avatar]);

  const slots = vault[avatar] || [];
  const slotData = selSlot != null ? slots.find(s => s.slot === selSlot) || null : null;
  const mine = myListings.filter(l => avatars.includes(l.avatar));

  async function post(p: PostListingInput) {
    if (busy) return;
    const src = (vault[p.avatar] || []).find(s => s.slot === p.slot);
    if (!src) return;
    setBusy(true);
    try {
      await api.postListing(p);
      toast(`Listed ${src.item.name}${p.stack > 1 ? ` ×${p.stack}` : ''} -- ${fmtNum(p.fee)} cr fee collected`);
      setSelSlot(null);
      await reload();
    } catch (err) {
      toast(err instanceof Error ? err.message : 'Listing failed');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className={styles.sell}>
      <div className={styles.sellLeft}>
        <div className="vault hud-panel">
          <div className="vault__head">
            <span className="vault__title">Vault Inventory</span>
            <span className="vault__avatar">{avatar}</span>
          </div>
          <div className="vgrid">
            {slots.length === 0
              ? <div style={{ gridColumn: '1 / -1', color: 'var(--text-faint)', fontFamily: 'var(--font-mono)', fontSize: 12, padding: 12 }}>Vault empty</div>
              : slots.map(s => {
                const r = RARITY[s.item.rarity];
                const untradable = s.item.noTrade === true;
                return (
                  <div key={s.slot}
                    className={'vslot' + (selSlot === s.slot ? ' vslot--sel' : '') + (untradable ? ' vslot--locked' : '')}
                    style={{ '--rcolor': r.color, '--rglow': r.glow } as Vars}
                    title={untradable
                      ? `${s.item.name} -- Not Tradable, cannot be listed`
                      : `${s.item.name}${s.quality != null ? ` -- Q${s.quality}%` : ''}`}
                    onClick={() => untradable
                      ? toast('That item is flagged Not Tradable and cannot be listed.')
                      : setSelSlot(s.slot)}>
                    <span className="icon__glyph">{s.item.glyph}</span>
                    {s.stack > 1 && <span className="vslot__qty">{s.stack}</span>}
                    {untradable && <span className="vslot__lock" title="Not Tradable">⊘</span>}
                  </div>
                );
              })}
          </div>
        </div>

        {slotData
          ? <PostForm slotData={slotData} avatar={avatar} onPost={post} busy={busy} />
          : <div className={`hud-panel ${styles.sellHint}`}><div><div className="empty__glyph">▲</div>Select a vault item<br />to set your listing terms</div></div>}
      </div>

      <div className={`${styles.sellMain} hud-panel`}>
        <div className={styles.sellMainhead}>
          <span>Your Listings -- {mine.length}</span>
          <span>{slotData ? 'Set terms, then list →' : 'Select a vault item to list ◂'}</span>
        </div>
        <div className={styles.ahtable}>
          <div className={`${styles.ahrow} ${styles.ahrowHead}`}>
            <div></div><div>Item</div><div>Avatar</div>
            <div className={styles.ahrowNum}>Quality</div>
            <div className={styles.ahrowNum}>Time</div>
            <div style={{ textAlign: 'right' }}>Current Bid</div>
            <div style={{ textAlign: 'right' }}>Buyout</div>
          </div>
          {mine.length === 0
            ? <div className="empty" style={{ padding: '60px 0' }}><div className="empty__inner"><div className="empty__glyph">◈</div>No active listings</div></div>
            : mine.map(l => <MyListingRow key={l.id} listing={l} />)}
        </div>
      </div>
    </div>
  );
}

/* ---------------- ROOT ---------------- */
export function AuctionHouse(props: {
  listings: Listing[];
  vault: Record<string, VaultSlot[]>;
  myListings: MyListing[];
  avatar: string;
  avatars: string[];
  reload: () => Promise<void>;
  toast: (msg: string) => void;
}) {
  const { listings, vault, myListings, avatar, avatars, reload, toast } = props;
  const [sub, setSub] = useState<'buy' | 'sell'>('buy');

  return (
    <div className="page">
      <div className="page__head">
        <div>
          <h1 className="page__title">Auction House</h1>
          <div className="page__sub">
            {sub === 'buy'
              ? `${listings.length} active listings -- 10% commission -- bids raise by >=1% of value`
              : 'Listing from your vault -- 8 / 12 / 24h -- 10% fee up front, 95% returned if unsold'}
          </div>
        </div>
        <div className="subtabs">
          <button className={'subtab' + (sub === 'buy' ? ' subtab--active' : '')} onClick={() => setSub('buy')}>
            <span className="subtab__glyph">⌕</span> Buy
          </button>
          <button className={'subtab' + (sub === 'sell' ? ' subtab--active' : '')} onClick={() => setSub('sell')}>
            <span className="subtab__glyph">▲</span> Sell
          </button>
        </div>
      </div>

      {sub === 'buy'
        ? <BuyView listings={listings} reload={reload} toast={toast} />
        : <SellView vault={vault} myListings={myListings}
            avatar={avatar} avatars={avatars} reload={reload} toast={toast} />}
    </div>
  );
}
