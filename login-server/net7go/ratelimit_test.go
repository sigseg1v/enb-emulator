// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Copyright (c) 2010 Net-7 Entertainment, Ltd.
// Modified by: Max Verigin, 2026 -- ported to go
//
// net7go -- tests for the AR-1 /AuthLogin flood defences (ratelimit.go).
package main

import (
	"net/http"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

func TestIPLimiter_BurstThenThrottle(t *testing.T) {
	clock := time.Unix(0, 0)
	l := newIPLimiter(1.0, 5) // 1/sec, burst 5
	l.now = func() time.Time { return clock }

	// First 5 from one IP pass (full bucket), 6th is throttled.
	for i := 0; i < 5; i++ {
		if !l.allow("1.2.3.4") {
			t.Fatalf("request %d should be allowed within burst", i+1)
		}
	}
	if l.allow("1.2.3.4") {
		t.Fatal("6th request should be throttled (bucket empty)")
	}

	// A different IP has its own full bucket (NAT'd household must still log in).
	if !l.allow("5.6.7.8") {
		t.Fatal("distinct IP should not be throttled by another IP's usage")
	}

	// After 2s, ~2 tokens refill.
	clock = clock.Add(2 * time.Second)
	if !l.allow("1.2.3.4") || !l.allow("1.2.3.4") {
		t.Fatal("tokens should refill over time")
	}
	if l.allow("1.2.3.4") {
		t.Fatal("only ~2 tokens should have refilled in 2s")
	}
}

func TestIPLimiter_DisabledWhenRateNonPositive(t *testing.T) {
	l := newIPLimiter(0, 0)
	for i := 0; i < 1000; i++ {
		if !l.allow("1.2.3.4") {
			t.Fatal("rate<=0 must disable limiting (always allow)")
		}
	}
}

func TestIPLimiter_EvictsStaleBuckets(t *testing.T) {
	clock := time.Unix(0, 0)
	l := newIPLimiter(1.0, 5)
	l.now = func() time.Time { return clock }
	l.allow("1.2.3.4")
	if len(l.buckets) != 1 {
		t.Fatalf("expected 1 bucket, got %d", len(l.buckets))
	}
	// Advance past ttl + gcEvery and touch a different IP to trigger GC.
	clock = clock.Add(l.ttl + l.gcEvery + time.Second)
	l.allow("9.9.9.9")
	if _, ok := l.buckets["1.2.3.4"]; ok {
		t.Fatal("stale bucket should have been evicted")
	}
}

func TestArgonGate_CapsConcurrency(t *testing.T) {
	g := newArgonGate(2, 50*time.Millisecond)
	if !g.acquire() || !g.acquire() {
		t.Fatal("first two acquires should succeed")
	}
	// Third over the cap: blocks for ~wait then fails.
	start := time.Now()
	if g.acquire() {
		t.Fatal("third acquire should fail (cap=2)")
	}
	if elapsed := time.Since(start); elapsed < 40*time.Millisecond {
		t.Fatalf("over-cap acquire returned too fast (%v); should wait ~50ms", elapsed)
	}
	g.release()
	if !g.acquire() {
		t.Fatal("acquire should succeed after a release frees a slot")
	}
}

func TestArgonGate_NeverExceedsCapUnderFlood(t *testing.T) {
	const cap = 3
	g := newArgonGate(cap, 100*time.Millisecond)
	var inflight, maxSeen int64
	var wg sync.WaitGroup
	for i := 0; i < cap+20; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if !g.acquire() {
				return // shed
			}
			n := atomic.AddInt64(&inflight, 1)
			for {
				m := atomic.LoadInt64(&maxSeen)
				if n <= m || atomic.CompareAndSwapInt64(&maxSeen, m, n) {
					break
				}
			}
			time.Sleep(20 * time.Millisecond)
			atomic.AddInt64(&inflight, -1)
			g.release()
		}()
	}
	wg.Wait()
	if maxSeen > cap {
		t.Fatalf("concurrent Argon2 holders %d exceeded cap %d", maxSeen, cap)
	}
}

func TestClientIP_PrefersTrustedHeader(t *testing.T) {
	r, _ := http.NewRequest("GET", "/AuthLogin", nil)
	r.RemoteAddr = "10.0.0.1:5555" // the relay
	r.Header.Set(clientIPHeader, "203.0.113.7")
	if got := clientIP(r); got != "203.0.113.7" {
		t.Fatalf("clientIP = %q, want trusted header 203.0.113.7", got)
	}
}

func TestClientIP_FallsBackToRemoteAddr(t *testing.T) {
	cases := map[string]string{
		"203.0.113.7:443": "203.0.113.7", // IPv4 host:port -> strip port
		"[::1]:443":       "[::1]",       // bracketed IPv6 -> strip trailing port
		"2001:db8::1":     "2001:db8::1", // bare IPv6, no port -> keep verbatim
	}
	for remote, want := range cases {
		r, _ := http.NewRequest("GET", "/AuthLogin", nil)
		r.RemoteAddr = remote
		if got := clientIP(r); got != want {
			t.Errorf("clientIP(%q) = %q, want %q", remote, got, want)
		}
	}
}
