# Phase AL -- WASM-in-browser proxy + REPL (investigation only)

Status: **investigation complete, 2026-06-07. Verdict: NOT feasible as asked.**
No code written. This file records the findings so the question doesn't get
re-opened from scratch.

## The two questions

1. Can the **proxy** run in a web browser compiled to WASM, making **real UDP
   connections to the sector servers live**, with nothing (no C#) installed on
   the player's host?
2. If so, rebuild the REPL on `nickprotop/ConsoleEx` (now **SharpConsoleUI**)
   and compile it to WASM for an in-browser, install-free C# REPL.

## Q1 verdict: a browser cannot make raw UDP connections. Full stop.

A WASM module has no syscalls. Every byte of I/O goes through a JS API the
browser chooses to expose. The browser exposes exactly four network primitives,
and **none of them can emit a raw UDP datagram to an arbitrary host:port**:

| Browser primitive | Transport | Can reach the existing UDP sector server? |
|---|---|---|
| `fetch` / XHR    | TCP (HTTP/1-2) | No -- TCP, and HTTP-shaped |
| WebSocket        | TCP (framed)   | No -- TCP to a WebSocket server only |
| WebRTC DataChannel | SCTP over DTLS over UDP, **peer-to-peer** | No -- needs an ICE/STUN/TURN + WebRTC peer on the other end, not a plain UDP listener |
| WebTransport     | QUIC (UDP, HTTP/3) | No -- only to a WebTransport/HTTP-3 server endpoint |

Emscripten *does* expose a BSD-sockets API (`socket()`, `SOCK_DGRAM`), so a
naive port "compiles." But emscripten's sockets are emulated **over WebSocket**
and require a `websockify`-style relay on the far end -- a `SOCK_DGRAM` there is
tunneled over a reliable TCP WebSocket to that relay, **not** a real UDP
datagram to the server. So even the parts that look like UDP are not.

### Consequence for our proxy specifically

The proxy's whole server-facing half is UDP: sector/global/MVAS on the 35xx/38xx
ports. A browser-WASM proxy cannot open those sockets. It would need a
**relay** that terminates WebSocket/WebTransport/WebRTC from the browser and
re-emits UDP to the server. That relay is a process running on *some* host
(normally co-located with the server). So:

- "Nothing installed on the **player's** host" -- achievable (it's all in the tab).
- "The proxy makes **real, direct UDP** to the sector server from the browser" --
  **not achievable**. There is always a server-side relay in the path.

And once a relay exists, the natural architecture is to run the **existing
native proxy on the relay** (which is exactly what it does today) and make the
browser a thin client to it -- the opposite of "the proxy compiled to WASM."

### DTLS makes it worse, not better

Phase AH made the proxy<->server leg **DTLS, fail-closed**, with per-(ip,port)
peer keying and a per-packet auth token bound to the source. A browser relay
forces a bad choice:

- **Dumb L4 relay** (pass DTLS records through): the server now sees DTLS from
  the relay's IP, not the client's -- breaks the `(ip,port)` peer map and the
  token's source assumptions. Re-engineering required.
- **Terminating relay** (DTLS ends at the relay, re-established to the server):
  the relay holds the client's DTLS credentials and auth token -- it is now a
  **trusted MITM and a real security boundary**, exactly the kind of thing the
  AH/AJ hardening exists to avoid. This is not a dumb bridge; it's a new
  privileged component to design, threat-model, and operate.

### The one honest reframe (still not "proxy in WASM")

**WebTransport** (HTTP/3 / QUIC, UDP-based) is the closest browser primitive to
UDP -- it gives unreliable datagrams. You *could* stand up a WebTransport server
endpoint that bridges browser datagrams to the existing UDP server. But that
endpoint is a **new server-side component that re-implements the proxy's
server-facing half**; the browser speaks QUIC to *it*, never raw UDP to the
sector server. It is not "the proxy compiled to WASM," and it inherits the same
DTLS-boundary problem above.

## Q2 verdict: moot, and ConsoleEx can't target WASM anyway.

Q2 is gated on Q1, so it's already moot. Independently, two hard blockers:

1. **ConsoleEx / SharpConsoleUI does not target WASM.** Per its own repo it
   "targets real terminals only -- not Blazor/WASM." It renders via
   `NetConsoleDriver`, raw libc I/O on Unix, the Windows Console API, and an
   embedded PTY -- none of which exist in a browser sandbox. Running it in WASM
   would mean writing a brand-new web rendering driver for it (large), or it's a
   non-starter. It is the wrong library for a browser target.
2. **Even a plain Blazor-WASM REPL** (forget ConsoleEx, render with xterm.js)
   still needs the proxy/network underneath -- which is Q1's wall.

## What IS actually feasible (if the goal is "in-browser, install-free")

The *spirit* of the request -- no install, a C# REPL in a tab -- is reachable;
just not by putting the proxy in WASM. Two viable shapes, both keep the proxy
**native and server-side**:

- **A. Blazor-WASM REPL + WebSocket to a server-side relay.** The REPL UI (C#
  compiled to WASM, rendered with xterm.js) runs in the tab and talks over a
  WebSocket to a relay that runs the existing native proxy and speaks UDP+DTLS
  to the servers. "No install, C# online" -- yes; "proxy in the browser" -- no.
- **B. Server-side CLI container the browser shells into** (ttyd / wetty style:
  WebSocket-to-PTY). Then SharpConsoleUI works (real PTY), the proxy is native,
  the browser is a dumb terminal. Zero C# in the browser, zero install -- but the
  compute is server-side, not in WASM.

Both are real projects, not free. Neither is "compile the proxy to WASM." If the
hard requirement is genuinely "no server-side component in the path at all,"
then the answer is simply **no** -- browsers cannot speak raw UDP, and the game
servers speak UDP.

## Recommendation

Do not pursue a WASM proxy. If an in-browser client is wanted later, scope it as
**option A** (Blazor-WASM REPL over a WebSocket relay) or **option B** (web PTY
to a server-side CLI), and budget the relay as a first-class, security-reviewed
component -- not a dumb bridge, because DTLS + the per-packet auth token make the
relay a trust boundary.
