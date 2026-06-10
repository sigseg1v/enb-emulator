// SPDX-License-Identifier: MIT
// Freya Online -- rarity model tests.
//
// These pin the exact tier/level-cap rule and, crucially, that the client
// mirror agrees with the Go server (server/rarity.go). If either side drifts,
// the AH renders a colour the backend would not assign. Keep the table below
// in lockstep with the Go table; both are derived from the same spec comment.

import { describe, it, expect } from 'vitest';
import { rarityFor, qualityClass } from './rarity';

describe('rarityFor', () => {
  it('treats a null quality as common regardless of level', () => {
    expect(rarityFor(1, null)).toBe('common');
    expect(rarityFor(99, null)).toBe('common');
  });

  it('maps quality thresholds to tiers (uncapped, high level)', () => {
    const lvl = 10; // above every cap, so tier == raw tier
    expect(rarityFor(lvl, 129)).toBe('common');
    expect(rarityFor(lvl, 130)).toBe('uncommon');
    expect(rarityFor(lvl, 149)).toBe('uncommon');
    expect(rarityFor(lvl, 150)).toBe('rare');
    expect(rarityFor(lvl, 179)).toBe('rare');
    expect(rarityFor(lvl, 180)).toBe('epic');
    expect(rarityFor(lvl, 255)).toBe('epic');
  });

  it('caps a high-quality item by its level', () => {
    // quality 200 is epic-tier, but the level cap pulls it down.
    expect(rarityFor(1, 200)).toBe('uncommon'); // L1-3 -> uncommon cap
    expect(rarityFor(3, 200)).toBe('uncommon');
    expect(rarityFor(4, 200)).toBe('rare'); // L4-6 -> rare cap
    expect(rarityFor(6, 200)).toBe('rare');
    expect(rarityFor(7, 200)).toBe('epic'); // L7+ -> epic reachable
  });

  it('never raises a low tier to the cap (cap is a ceiling, not a floor)', () => {
    expect(rarityFor(10, 130)).toBe('uncommon'); // not forced up to epic
    expect(rarityFor(1, 130)).toBe('uncommon'); // tier == cap
    expect(rarityFor(1, 100)).toBe('common'); // below tier threshold
  });
});

describe('qualityClass', () => {
  it('buckets quality into the CSS class bands', () => {
    expect(qualityClass(170)).toBe('qual--exc');
    expect(qualityClass(200)).toBe('qual--exc');
    expect(qualityClass(130)).toBe('qual--high');
    expect(qualityClass(169)).toBe('qual--high');
    expect(qualityClass(100)).toBe('qual--ok');
    expect(qualityClass(129)).toBe('qual--ok');
    expect(qualityClass(99)).toBe('qual--low');
    expect(qualityClass(0)).toBe('qual--low');
  });
});
