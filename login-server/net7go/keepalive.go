// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Copyright (c) 2010 Net-7 Entertainment, Ltd.
// Modified by: Max Verigin, 2026 -- ported to go
//
// net7go -- standalone Go reimplementation of Net7SSL (see config.go header).
//
// keepalive.go -- the AF_UNIX SOCK_DGRAM server-liveness keepalive, a port of
// the Net7SSL main loop (Net7SSL.cpp) + MailslotManager + common/PosixIpc.cpp.
// Every ~10s net7go sends a "Ping" datagram (NUL-terminated, matching
// PosixIpc::WriteMessage which sends strlen+1 bytes) to the game server's recv
// socket; the server updates its g_SSL_receive_time on ANY datagram and pongs
// back. net7go drains those pongs and tracks the server's liveness.
//
// Difference from the C++: the C++ login EXITED when no pong arrived for 60s
// (Win32 relaunched it; the Linux build's RelaunchNet7SSL is a no-op). net7go
// instead logs a warning and keeps serving -- killing the auth endpoint while
// the game server reboots would be worse, and container orchestration owns
// process lifecycle now. net7go is the SOLE owner of the recv socket
// post-cutover (the C++ login it replaces is gone), so there is no two-binder
// conflict.
package main

import (
	"context"
	"log"
	"net"
	"os"
	"path/filepath"
	"time"
)

const (
	keepalivePingInterval = 10 * time.Second
	keepaliveStaleAfter   = 60 * time.Second
)

// startKeepalive binds the recv socket and runs the ping/drain loop until ctx
// is cancelled. Returns immediately (the loop runs in a goroutine). A bind
// failure is logged and the keepalive is skipped -- it must not take down auth.
func startKeepalive(ctx context.Context, cfg Config) {
	// Ensure the shared IPC dir exists and is world-writable so the game server
	// container (a different uid) can also bind its socket there. The C++ login
	// container did this in its shell entrypoint; net7go has no shell entrypoint,
	// so it owns the mkdir/chmod itself (same-host trust model -- see PosixIpc.h).
	dir := filepath.Dir(cfg.IPCRecvSock)
	if err := os.MkdirAll(dir, 0o777); err == nil {
		_ = os.Chmod(dir, 0o777)
	}

	// AF_UNIX bind fails if the path already exists; remove a stale socket
	// left by a previous crash (same as PosixIpc::OpenRecv unlinking first).
	_ = os.Remove(cfg.IPCRecvSock)

	recvAddr := &net.UnixAddr{Name: cfg.IPCRecvSock, Net: "unixgram"}
	conn, err := net.ListenUnixgram("unixgram", recvAddr)
	if err != nil {
		log.Printf("keepalive: bind recv socket %s failed: %v; keepalive disabled (server will see login as DOWN)", cfg.IPCRecvSock, err)
		return
	}

	// World-writable so the server container (a different uid) can sendto us,
	// matching the 0777 the compose entrypoint sets on /run/net7-ipc.
	_ = os.Chmod(cfg.IPCRecvSock, 0o777)

	sendAddr := &net.UnixAddr{Name: cfg.IPCSendSock, Net: "unixgram"}

	go keepaliveLoop(ctx, conn, sendAddr)
}

func keepaliveLoop(ctx context.Context, conn *net.UnixConn, sendAddr *net.UnixAddr) {
	defer conn.Close()

	ping := time.NewTicker(keepalivePingInterval)
	defer ping.Stop()

	// "Ping\0" -- PosixIpc sends strlen(message)+1 bytes (NUL included).
	pingMsg := append([]byte("Ping"), 0)

	lastPong := time.Now()
	warned := false
	buf := make([]byte, 1024)

	for {
		select {
		case <-ctx.Done():
			return
		case <-ping.C:
			// Send the keepalive. The peer (server recv socket) may not be up
			// yet -- that is expected during startup; don't spam on failure.
			if _, err := conn.WriteToUnix(pingMsg, sendAddr); err != nil {
				// transient; the next tick retries
				_ = err
			}

			// Drain any pending pongs (non-blocking) and refresh liveness.
			_ = conn.SetReadDeadline(time.Now().Add(50 * time.Millisecond))
			for {
				n, _, rerr := conn.ReadFromUnix(buf)
				if rerr != nil {
					break // EAGAIN / deadline -> nothing pending
				}
				if n > 0 {
					lastPong = time.Now()
				}
			}

			if time.Since(lastPong) > keepaliveStaleAfter {
				if !warned {
					log.Printf("keepalive: no pong from the game server for >%s; it may be down (continuing to serve auth)", keepaliveStaleAfter)
					warned = true
				}
			} else if warned {
				log.Printf("keepalive: game server keepalive recovered")
				warned = false
			}
		}
	}
}
