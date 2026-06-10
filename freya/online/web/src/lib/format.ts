// SPDX-License-Identifier: MIT
// Freya Online -- formatting helpers.

export function fmtNum(n: number): string {
  return n.toLocaleString('en-US');
}

import type { Item } from '../types';

/** Opening bid suggestion: 110% of vendor value. */
export function bidStartOf(item: Item): number {
  return Math.round(item.vendor * 1.1);
}

/** Suggested buyout: 200% of vendor value. */
export function buyoutOf(item: Item): number {
  return Math.round(item.vendor * 2.0);
}

/** Minimum bid increment: 1% of item value, never below 1cr. */
export function minIncrementOf(item: Item): number {
  return Math.max(1, Math.round(item.vendor * 0.01));
}
