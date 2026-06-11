// SPDX-License-Identifier: MIT
// Freya Online -- shared presentational components.
import type { CSSProperties } from 'react';
import type { Band, Item } from '../types';
import { RARITY, qualityClass } from '../lib/rarity';
import { fmtNum } from '../lib/format';

type Vars = CSSProperties & Record<string, string | number>;

export function Credits({ value, className = '' }: { value: number; className?: string }) {
  return (
    <span className={'cr ' + className}>{fmtNum(value)}<span className="cr__unit">cr</span></span>
  );
}

export function Wordmark({ size = 26, sub = true }: { size?: number; sub?: boolean }) {
  return (
    <span className="wordmark" style={{ '--wm-size': size + 'px' } as Vars}>
      <span className="wordmark__main"><span className="wordmark__orbit" aria-hidden="true" />FREYA</span>
      {sub && <span className="wordmark__sub">ONLINE</span>}
    </span>
  );
}

export function ServerStatus({ status = 'ONLINE', players = 0 }: { status?: 'ONLINE' | 'OFFLINE'; players?: number }) {
  const on = status === 'ONLINE';
  return (
    <div className="srv">
      <span className="srv__group">
        <span className={'srv__dot ' + (on ? 'srv__dot--on' : 'srv__dot--off')} />
        <span className={on ? 'srv__val--on' : 'srv__val--off'}>{status}</span>
      </span>
      <span className="srv__group">
        <span className="srv__label">Players</span>
        <span className="srv__players">{on ? fmtNum(players) : '-'}</span>
      </span>
    </div>
  );
}

export function ItemIcon({ item, className = '' }: { item: Item; className?: string }) {
  const r = RARITY[item.rarity];
  return (
    <div className={'icon ' + className} style={{ '--rcolor': r.color, '--rglow': r.glow } as Vars}>
      <span className="icon__glyph">{item.glyph}</span>
    </div>
  );
}

// `value` is the game-native quality fraction (1.0 == 100%); render it as a
// percent for humans. qualityClass thresholds are likewise fractional.
export function Quality({ value }: { value: number }) {
  return <span className={'qual ' + qualityClass(value)}>{Math.round(value * 100)}%</span>;
}

export function TimeBand({ band, showLabel = true }: { band: Band; showLabel?: boolean }) {
  const fills = { low: 1, med: 2, high: 3 }[band];
  const heights = [6, 9, 13];
  return (
    <span className={'band band--' + band} title="Approximate time remaining">
      <span className="band__bars">
        {heights.map((h, i) => (
          <span key={i} className={'band__bar' + (i < fills ? ' on' : '')} style={{ height: h + 'px' }} />
        ))}
      </span>
      {showLabel && <span className="band__label">{band}</span>}
    </span>
  );
}
