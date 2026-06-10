// SPDX-License-Identifier: MIT
// Freya Online -- formatting / bid-math tests.

import { describe, it, expect } from 'vitest';
import { fmtNum, bidStartOf, buyoutOf, minIncrementOf } from './format';
import type { Item } from '../types';

// A minimal Item with only the field the math reads (vendor); the rest is
// structurally required but irrelevant to these pure functions.
function item(vendor: number): Item {
  return {
    id: '1', name: 't', rarity: 'common', cat: 'ore', slot: '', level: 1,
    glyph: '', description: '', prices: { buy: 0, sell: 0, manufacture: 0 }, vendor,
  };
}

describe('fmtNum', () => {
  it('groups thousands with commas', () => {
    expect(fmtNum(0)).toBe('0');
    expect(fmtNum(1000)).toBe('1,000');
    expect(fmtNum(1234567)).toBe('1,234,567');
  });
});

describe('bid / buyout suggestions', () => {
  it('opening bid is 110% of vendor, rounded', () => {
    expect(bidStartOf(item(100))).toBe(110);
    expect(bidStartOf(item(95))).toBe(105); // 104.5 -> 105
  });

  it('buyout suggestion is 200% of vendor', () => {
    expect(buyoutOf(item(100))).toBe(200);
    expect(buyoutOf(item(0))).toBe(0);
  });
});

describe('minIncrementOf', () => {
  it('is 1% of vendor, floored at 1 credit', () => {
    expect(minIncrementOf(item(10000))).toBe(100);
    expect(minIncrementOf(item(100))).toBe(1); // 1% == 1
    expect(minIncrementOf(item(10))).toBe(1); // 0.1 -> max(1, 0) == 1
    expect(minIncrementOf(item(0))).toBe(1); // never below 1
    expect(minIncrementOf(item(15050))).toBe(151); // 150.5 -> 151
  });
});
