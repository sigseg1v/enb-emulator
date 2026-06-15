// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Copyright (c) 2010 Net-7 Entertainment, Ltd.
// Modified by: Max Verigin, 2026 -- ported to go
//
// net7go -- standalone Go reimplementation of Net7SSL (see config.go header).
//
// ratelimit.go -- login-flood / resource-exhaustion defence for /AuthLogin
// (Phase AR-1). Two independent layers:
//
//  1. ipLimiter: a per-client-IP token bucket that bounds brute-force attempts
//     from a single origin. net7go sits behind the Freya Online TLS terminator,
//     so r.RemoteAddr is ALWAYS the relay -- the real client IP arrives in the
//     trusted X-Freya-Client-IP header the relay overwrites at the edge (see
//     freya/online/server/legacy_proxy.go). We key the bucket on that header so
//     several real players behind one NAT each get their own bucket, while a
//     brute-forcer from one IP is throttled.
//
//  2. argonGate: a global semaphore capping CONCURRENT Argon2id verifications.
//     Each verify runs Argon2id at 64 MiB, so a few hundred concurrent floods
//     would exhaust box memory regardless of per-IP rate. The gate is the real
//     exhaustion backstop: requests over the cap wait briefly then get a cheap
//     rejection rather than queueing unboundedly (which would be the DoS again).
//
// On throttle/saturation handleAuthLogin returns the EXACT same bytes as a
// normal failed login ("Valid=False\r\n"); it invents no new wire surface, so a
// throttled client behaves as it already does on a busy/failed auth. The legacy
// wire format stays entirely inside this CC BY-NC-SA binary.
package main

import (
	"net/http"
	"sync"
	"time"
)

// clientIPHeader is set by the Freya Online relay to the edge-observed client
// IP. The relay OVERWRITES any client-supplied value, so it is trustworthy here
// (net7go is reachable only via the relay on the internal network). MUST match
// the header name the relay writes in freya/online/server/legacy_proxy.go.
const clientIPHeader = "X-Freya-Client-IP"

// tokenBucket is a monotonic-refill token bucket.
type tokenBucket struct {
	tokens float64
	last   time.Time
}

// ipLimiter keeps one token bucket per client IP, refilling at ratePerSec up to
// burst. Stale buckets are evicted so the map cannot grow unbounded under a
// spray of distinct source IPs.
type ipLimiter struct {
	mu         sync.Mutex
	buckets    map[string]*tokenBucket
	ratePerSec float64
	burst      float64
	ttl        time.Duration // evict buckets untouched for this long
	gcEvery    time.Duration
	lastGC     time.Time
	now        func() time.Time // injectable clock for tests
}

func newIPLimiter(ratePerSec, burst float64) *ipLimiter {
	return &ipLimiter{
		buckets:    make(map[string]*tokenBucket),
		ratePerSec: ratePerSec,
		burst:      burst,
		ttl:        15 * time.Minute,
		gcEvery:    5 * time.Minute,
		now:        time.Now,
	}
}

// allow consumes one token for ip, returning false when the bucket is empty.
// A non-positive rate disables limiting (always allow).
func (l *ipLimiter) allow(ip string) bool {
	if l == nil || l.ratePerSec <= 0 {
		return true
	}
	now := l.now()
	l.mu.Lock()
	defer l.mu.Unlock()

	if now.Sub(l.lastGC) >= l.gcEvery {
		for k, b := range l.buckets {
			if now.Sub(b.last) > l.ttl {
				delete(l.buckets, k)
			}
		}
		l.lastGC = now
	}

	b := l.buckets[ip]
	if b == nil {
		// First sight of this IP: full bucket, consume one.
		l.buckets[ip] = &tokenBucket{tokens: l.burst - 1, last: now}
		return true
	}
	elapsed := now.Sub(b.last).Seconds()
	b.tokens += elapsed * l.ratePerSec
	if b.tokens > l.burst {
		b.tokens = l.burst
	}
	b.last = now
	if b.tokens >= 1 {
		b.tokens--
		return true
	}
	return false
}

// argonGate caps concurrent Argon2id verifications. acquire blocks up to wait
// for a slot; if none frees in time it returns false so the caller can reject
// cheaply instead of piling on more memory pressure.
type argonGate struct {
	ch   chan struct{}
	wait time.Duration
}

func newArgonGate(maxConcurrent int, wait time.Duration) *argonGate {
	if maxConcurrent < 1 {
		maxConcurrent = 1
	}
	return &argonGate{ch: make(chan struct{}, maxConcurrent), wait: wait}
}

func (g *argonGate) acquire() bool {
	if g == nil {
		return true
	}
	select {
	case g.ch <- struct{}{}:
		return true
	default:
	}
	t := time.NewTimer(g.wait)
	defer t.Stop()
	select {
	case g.ch <- struct{}{}:
		return true
	case <-t.C:
		return false
	}
}

func (g *argonGate) release() {
	if g == nil {
		return
	}
	<-g.ch
}

// clientIP extracts the real client IP for rate-limiting: the relay-trusted
// header first, then RemoteAddr (dev / direct-dial fallback).
func clientIP(r *http.Request) string {
	if v := r.Header.Get(clientIPHeader); v != "" {
		return v
	}
	host := r.RemoteAddr
	if i := lastColon(host); i >= 0 {
		host = host[:i]
	}
	return host
}

// lastColon returns the index of the port separator in a host:port string,
// tolerating bracketed IPv6 literals ([::1]:443). -1 if there is no port.
func lastColon(s string) int {
	// If it looks like a bare IPv6 address (multiple colons, no bracket), there
	// is no port to strip.
	if len(s) > 0 && s[0] != '[' {
		first := -1
		count := 0
		for i := 0; i < len(s); i++ {
			if s[i] == ':' {
				count++
				if first < 0 {
					first = i
				}
			}
		}
		if count > 1 {
			return -1 // bare IPv6, no port
		}
		return first
	}
	// Bracketed form: port (if any) is after the closing bracket.
	for i := len(s) - 1; i >= 0; i-- {
		if s[i] == ':' {
			return i
		}
		if s[i] == ']' {
			return -1
		}
	}
	return -1
}
