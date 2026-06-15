// SPDX-License-Identifier: MIT
// Freya Online -- login-flood / resource-exhaustion defence (Phase AR-1).
//
// The website /api/login (and the authenticated /api/account/password) run
// Argon2id at 64 MiB per verify, so an unauthenticated flood is a memory/CPU
// exhaustion vector. Two independent layers, mirroring the net7go game-auth
// path (login-server/net7go/ratelimit.go) but for the browser API:
//
//  1. ipLimiter -- a per-client-IP token bucket bounding brute-force from one
//     origin. freya-online is the TLS edge (binds :443 directly, no reverse
//     proxy in front), so r.RemoteAddr is the genuine client IP; we do NOT
//     trust a spoofable X-Forwarded-For. Over the limit -> HTTP 429.
//
//  2. argonGate -- a global semaphore capping CONCURRENT Argon2id calls (the
//     real memory backstop). Over the cap, callers wait briefly then get a
//     cheap 503 rather than queueing unboundedly (which would be the DoS again).
//
// This is generic original work (a token bucket + a buffered-channel semaphore),
// not a port of any inherited code.

package main

import (
	"net"
	"net/http"
	"sync"
	"time"
)

type tokenBucket struct {
	tokens float64
	last   time.Time
}

// ipLimiter keeps one refilling token bucket per client IP, evicting stale
// buckets so the map cannot grow unbounded under a spray of distinct IPs.
type ipLimiter struct {
	mu         sync.Mutex
	buckets    map[string]*tokenBucket
	ratePerSec float64
	burst      float64
	ttl        time.Duration
	gcEvery    time.Duration
	lastGC     time.Time
	now        func() time.Time
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

// allow consumes one token for ip; false when the bucket is empty. A
// non-positive rate disables limiting.
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

// argonGate caps concurrent Argon2id calls. acquire blocks up to wait for a
// slot; false means shed the request cheaply.
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

// remoteIP returns the client IP from RemoteAddr (host part), tolerating the
// missing-port case. freya-online is the TLS edge so this is the real client.
func remoteIP(r *http.Request) string {
	if host, _, err := net.SplitHostPort(r.RemoteAddr); err == nil {
		return host
	}
	return r.RemoteAddr
}
