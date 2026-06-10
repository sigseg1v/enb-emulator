// SPDX-License-Identifier: MIT
// Freya Online -- Mailbox (shared per account).
import { useState } from 'react';
import type { CSSProperties } from 'react';
import type { Attachment, Mail } from '../types';
import { RARITY } from '../lib/rarity';
import { Credits } from '../components/ui';
import { fmtNum } from '../lib/format';
import * as api from '../api';

type Vars = CSSProperties & Record<string, string | number>;

function MailRow({ mail, active, onClick }: { mail: Mail; active: boolean; onClick: () => void }) {
  const hasItem = mail.attachments.length > 0;
  return (
    <div className={'mail' + (active ? ' mail--active' : '')} onClick={onClick}>
      <span className={'mail__unread' + (mail.read ? ' mail__unread--read' : '')} />
      <div className="mail__main">
        <div className={'mail__subj' + (mail.read ? '' : ' mail__subj--unread')}>{mail.subject}</div>
        <div className="mail__from">{mail.sender}</div>
      </div>
      <div className="mail__right">
        {hasItem && <span className="mail__clip" title="Has item">◈</span>}
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
      <div className="aslot" style={spentStyle} onClick={handle}>
        <div className="aslot__sq" style={{ '--rcolor': 'var(--warm)', '--rglow': 'oklch(0.80 0.125 72 / 0.3)' } as Vars}>
          <span className="aslot__cr">cr</span>
        </div>
        <div className="aslot__name"><Credits value={att.amount} /></div>
        <div className="aslot__loot">{label}</div>
      </div>
    );
  }
  const r = RARITY[att.item.rarity];
  return (
    <div className="aslot" style={spentStyle} onClick={handle}>
      <div className="aslot__sq" style={{ '--rcolor': r.color, '--rglow': r.glow } as Vars}>
        <span className="icon__glyph">{att.item.glyph}</span>
        {att.stack > 1 && <span className="aslot__qty">{att.stack}</span>}
      </div>
      <div className="aslot__name" style={{ color: r.color }}>{att.item.name}</div>
      <div className="aslot__loot">{label}</div>
    </div>
  );
}

function lootLabel(att: Attachment): string {
  return att.type === 'credits' ? `${fmtNum(att.amount)} cr` : `${att.item.name} -> vault`;
}

export function Mailbox({
  mail, setMail, character, reload, toast,
}: {
  mail: Mail[];
  setMail: React.Dispatch<React.SetStateAction<Mail[]>>;
  character: string;
  reload: () => Promise<void>;
  toast: (msg: string) => void;
}) {
  const [activeId, setActiveId] = useState<string | null>(mail[0] ? mail[0].id : null);
  const [busy, setBusy] = useState(false);
  const active = mail.find(m => m.id === activeId) || null;

  function open(m: Mail) {
    setActiveId(m.id);
    if (!m.read) {
      // Optimistic: flip the dot immediately, persist in the background. A failed
      // read-mark is cosmetic, so it only logs -- the next reload reconciles it.
      setMail(prev => prev.map(x => x.id === m.id ? { ...x, read: true } : x));
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

  const unread = mail.filter(m => !m.read).length;

  return (
    <div className="page">
      <div className="page__head">
        <div>
          <h1 className="page__title">Mailbox</h1>
          <div className="page__sub">Account inbox &middot; {character} &middot; {unread} unread</div>
        </div>
      </div>

      <div className="mbx">
        <div className="mbx__list hud-panel">
          <div className="mbx__listhead">
            <span>{mail.length} Messages</span>
            <span>Subject / Sender</span>
          </div>
          <div className="mbx__scroll">
            {mail.length === 0
              ? <div className="empty"><div className="empty__inner"><div className="empty__glyph">✉</div>Inbox empty</div></div>
              : mail.map(m => <MailRow key={m.id} mail={m} active={m.id === activeId} onClick={() => open(m)} />)}
          </div>
        </div>

        <div className="mread hud-panel hud-corners">
          {!active
            ? <div className="empty"><div className="empty__inner"><div className="empty__glyph">✉</div>Select a message</div></div>
            : (
              <>
                <div className="mread__head">
                  <h2 className="mread__subj">{active.subject}</h2>
                  <dl className="mread__meta">
                    <dt>From</dt><dd>{active.sender}</dd>
                    <dt>To</dt><dd>{active.recipient}</dd>
                    <dt>Expires</dt><dd>{active.expiresInDays} days</dd>
                  </dl>
                </div>

                {active.attachments.length > 0 && (
                  <div className="mread__warn">
                    <span>⚠</span>
                    <span>This message contains attachments. Unclaimed items and credits are <b>permanently removed after 90 days</b>. Collect them to your account before they expire.</span>
                  </div>
                )}

                <div className="mread__body">{active.body}</div>

                {active.attachments.length > 0 && (
                  <div className="attach">
                    <h3 className="attach__title">Attachments -- {active.attachments.length}</h3>
                    <div className="attach__grid">
                      {active.attachments.map((att, i) => (
                        <AttachmentSlot key={att.index ?? i} att={att} onLoot={() => lootIndex(active.id, i, att)} />
                      ))}
                    </div>
                    {active.attachments.length > 1 &&
                      <button className="btn btn--sm attach__all" disabled={busy} onClick={() => lootAll(active)}>Loot All</button>}
                  </div>
                )}
              </>
            )}
        </div>
      </div>
    </div>
  );
}
