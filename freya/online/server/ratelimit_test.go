// SPDX-License-Identifier: MIT
// Freya Online -- tests for the AR-1 login-flood defences (ratelimit.go).
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

	for i := 0; i < 5; i++ {
		if !l.allow("1.2.3.4") {
			t.Fatalf("request %d should be allowed within burst", i+1)
		}
	}
	if l.allow("1.2.3.4") {
		t.Fatal("6th request should be throttled")
	}
	if !l.allow("5.6.7.8") {
		t.Fatal("a distinct IP must have its own bucket")
	}
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
			t.Fatal("rate<=0 must disable limiting")
		}
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
				return
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

func TestArgonGate_ShedsOverCapThenRecovers(t *testing.T) {
	g := newArgonGate(1, 30*time.Millisecond)
	if !g.acquire() {
		t.Fatal("first acquire should succeed")
	}
	if g.acquire() {
		t.Fatal("second acquire over cap=1 should be shed")
	}
	g.release()
	if !g.acquire() {
		t.Fatal("acquire should succeed after release")
	}
}

func TestRemoteIP_StripsPort(t *testing.T) {
	cases := map[string]string{
		"203.0.113.7:443": "203.0.113.7",
		"[::1]:443":       "::1",
		"203.0.113.9":     "203.0.113.9", // no port -> verbatim
	}
	for remote, want := range cases {
		r, _ := http.NewRequest("GET", "/api/login", nil)
		r.RemoteAddr = remote
		if got := remoteIP(r); got != want {
			t.Errorf("remoteIP(%q) = %q, want %q", remote, got, want)
		}
	}
}
