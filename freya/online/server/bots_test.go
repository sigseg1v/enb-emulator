// SPDX-License-Identifier: MIT
// Freya Online -- AhBot auto-bid probability curve tests (pure, no DB).

package main

import (
	"math"
	"testing"
)

// botBidChance must hit the spec's anchor points exactly, stay monotonically
// non-increasing across the interpolated ranges, plateau at 20% below half
// price, and drop to zero once a listing is priced above 120% of the AhBot's
// own minimum bid.
func TestBotBidChance_Anchors(t *testing.T) {
	cases := []struct {
		ratio float64
		want  float64
	}{
		{0.00, 0.20}, // free-roll floor: still capped at the 20% plateau
		{0.25, 0.20},
		{0.50, 0.20}, // "half price or less" -> 20%
		{0.75, 0.05},
		{0.90, 0.01},
		{1.20, 0.005},
		{1.21, 0.0}, // just over 120% -> ignore
		{2.00, 0.0},
	}
	for _, c := range cases {
		got := botBidChance(c.ratio)
		if math.Abs(got-c.want) > 1e-9 {
			t.Errorf("botBidChance(%.2f) = %.6f, want %.6f", c.ratio, got, c.want)
		}
	}
}

// Midpoints of each interpolated band must land halfway between their anchors,
// proving the curve is linear (not a step) between the spec points.
func TestBotBidChance_Interpolates(t *testing.T) {
	cases := []struct {
		ratio float64
		want  float64
	}{
		{0.625, (0.20 + 0.05) / 2}, // midpoint of [0.50,0.75]
		{0.825, (0.05 + 0.01) / 2}, // midpoint of [0.75,0.90]
		{1.05, (0.01 + 0.005) / 2}, // midpoint of [0.90,1.20]
	}
	for _, c := range cases {
		got := botBidChance(c.ratio)
		if math.Abs(got-c.want) > 1e-9 {
			t.Errorf("botBidChance(%.3f) = %.6f, want %.6f (linear midpoint)", c.ratio, got, c.want)
		}
	}
}

// The curve must never increase as a listing gets relatively more expensive.
func TestBotBidChance_Monotonic(t *testing.T) {
	prev := math.Inf(1)
	for r := 0.0; r <= 1.5; r += 0.01 {
		got := botBidChance(r)
		if got > prev+1e-12 {
			t.Fatalf("botBidChance not monotonic: chance(%.2f)=%.6f > previous %.6f", r, got, prev)
		}
		prev = got
	}
}
