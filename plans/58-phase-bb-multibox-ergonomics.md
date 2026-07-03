# Phase BB -- Multibox ergonomics (incremental add + single-slot relaunch)

Owner ask (2026-07-02):

> the just play-online 2 path sucks because this client crashes all the time so
> i often need to relaunch. It's very difficult to relaunch 1 player if I have 3
> still running via this method. I want to be able to say "just play-local" and
> launch more when I already have one running. [...] give me a simple way to
> relaunch clean for crashed clients when multiboxing that doesn't involve
> relaunching them all. [...] make sure it works for play-online as well.

## Background -- what already exists

- `play-local` (justfile) ALREADY auto-promotes: run it again while a client is
  up and it claims a free SLOT (0..7) under a lock dir
  (`$XDG_RUNTIME_DIR/freya-play-local-slots`), spins up a private docker proxy
  unit on its own `127.0.0.1:23500+slot*10` port block + a cloned WINE prefix
  (`~/.wine-enb-mboxN`), and does NOT rebuild/bounce the shared stack (only slot
  0 touches it). So incremental local add is done -- it was just unnamed and
  undocumented, and had no per-slot relaunch.
- The launcher itself (`MultiboxSlot.Resolve`) auto-detects the next free port
  block when `FREYA_GAME_PORT_BASE` is unset -- "press Play again -> fresh
  instance" is built in.
- `play-multibox-local N` = launch-all-at-once, self-cleaning on exit (tears the
  whole set down). Kept as-is (the batch convenience).

## The gaps this phase closes

- **BB-1 -- `play-online` incremental add.** `play-online` unconditionally
  `docker compose down` + `pkill -f FreyaProxy.exe` at the top, then launches all
  N in one foreground loop. That teardown is exactly what disconnects the
  already-running clients, and there is no way to add ONE online client to a
  running set. Fix: the default (no-count) `just play-online` claims a free slot
  and launches ONE more, without tearing down or pkilling when other online
  clients are already up. Only the FIRST online client (slot 0) frees the local
  docker ports. The explicit `just play-online N` batch form is unchanged.
- **BB-2 -- single-slot clean relaunch.** New `just relaunch-slot N [MODE]`:
  cleanly kill ONE crashed/wedged client (its own WINE prefix session via
  `wineserver -k`, its launcher process, and -- local only -- its docker mbox
  proxy unit), free its slot, and print the exact one-line re-add command. Never
  touches the other running slots. Works for both local and online (MODE selects
  whether a docker proxy unit exists).
- **BB-3 -- named entry point.** `just play-local-multi` alias so the
  incremental-add model is discoverable; a one-line wrapper over `play-local`
  with a doc comment that says "run it again to add another client".

## Design constraints (carried from CLAUDE.md / prior work)

- **Never persist the password.** `relaunch-slot` must NOT store or replay a
  password from disk. It replays account/character/mode only; hands-free
  re-login needs `PASS=...`/`PASS=PROMPT` re-supplied (same contract as
  `play-local`). This preserves the "password never saved" invariant.
- **Kill exactly one client, never the others.** Target by WINE PREFIX
  (`WINEPREFIX=<slot prefix> wineserver -k` only reaps that prefix's client.exe +
  FreyaProxy.exe) + the slot's own launcher PID + the slot's own docker mbox
  project. No global `pkill FreyaProxy.exe` (it would kill every running
  instance -- the original footgun).
- **Slot 0 is special.** Local slot 0 owns the shared docker stack; online slot 0
  owns the stock 3500/3801/3805 port block. Relaunching slot 0 must not assume a
  cloned prefix (it uses the base `~/.wine-enb`) and, for local, must not tear
  the shared stack down.

## Tasks

- [ ] BB-1 `play-online`: default form claims a free slot + adds one client;
      teardown/pkill gated to the first online client only.
- [ ] BB-2 `relaunch-slot N [MODE=local]`: clean single-slot kill + free + print
      re-add command (both local + online).
- [ ] BB-3 `play-local-multi` named alias + doc.
- [ ] BB-4 Update `docs/09-running-locally.md` multibox section.

## Notes

- No server/proxy/client wire change -- this is launch tooling only. No CV entry,
  no CLI byte-pin needed.
</content>
