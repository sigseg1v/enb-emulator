# Phase PB-2-cont -- Engine read + live verification (the "tomorrow" runbook)

Status: `[ ]` not started. Owner-driven, single sitting. Picks up exactly where
the 2026-06-08 session left off: the DLL now injects into `client.exe`; the only
thing left before the feed does real work is the in-process engine read, then a
live flight check against the real client.

Background and full design: `plans/41-player-reported-bugs.md` (PB-2 row) and
`plans/29-client-verification.md` (CV-MVAS-POS). Read those first if anything
below is unclear.

## Where we are

- Producer DLL (`bin/FreyaPosFeed.dll`) + injector (`bin/FreyaInject.exe`) build via
  `just build-posfeed-dll`. Both 32-bit PE (client.exe is PE32/i386).
- Injection is wired in the launcher: `play-local` stages the DLL into the WINE
  prefix and spawns `FreyaInject.exe`, which starts `client.exe` suspended,
  remote-LoadLibrary's the DLL, and resumes it. (WINE has no `AppInit_DLLs`; that
  was the dead end last session.)
- Proxy consumer binds loopback `udp/3807`, drains the latest sample, and streams
  `0x1004` to MVAS `3806`. No server change, no wire change.
- The engine read (`Net7ReadEngineShipState_Local` in the gitignored
  `client/detours/ClientEngineOffsets.local.h`) still returns false -> feed inert.
  This is the one remaining gap.

## Step 0 -- confirm the DLL actually loads (do this FIRST, before any offset work)

This was never observed working. Prove it before spending time on offsets.

```
WINEDEBUG=+loaddll just play-local 2>&1 | grep -i 'net7posfeed\|client.exe'
```

Click Play. Expect BOTH lines: `client.exe` at `00400000` AND a `FreyaPosFeed.dll`
load. If only `client.exe` shows, the inject failed -- stop and debug the injector
(`client/detours/FreyaInject.cpp`), do NOT proceed to offsets. Likely suspects if
it fails: WOW64 staging path (DLL must be in BOTH `system32` and `syswow64`),
32-vs-64-bit mismatch, or the injector's remote `LoadLibraryA` returning NULL
(`FreyaInject.exe` prints `HMODULE=0x...` on success / a NULL-return line on
failure -- run the injector leg with stderr visible).

## Step 1 -- the engine read is ALREADY FILLED (verify, don't author)

`client/detours/ClientEngineOffsets.local.h` already exists on this machine
(gitignored, so it is NOT in the repo and will not survive a fresh clone -- it
lives only here). It was filled 2026-06-08 from the historical feed recipe, so
there is normally nothing to author. It:

- Patches five client code sites once so the running client stashes its live ship
  transform pointer + GameID into a fixed `.data` scratch slot, then reads
  position/orientation through that slot every frame (the transform is behind a
  DYNAMIC heap pointer -- there is no static address).
- Reads the row-major 3x4 transform at `capturedPtr + 0x48`: world position =
  translation column (bytes 12 / 28 / 44), heading = first column (0 / 16 / 32).
- Has a **build fingerprint guard**: the 7 original bytes at the JMP-hijack site
  must match this build before any patch is written. On mismatch it patches
  NOTHING and the read returns false forever -- a wrong/updated client degrades to
  "no feed", never a crash. Per-frame reads are additionally guarded by
  IsBadReadPtr + a GameID range gate + a (0,0,0) reject.

What you DO need to do tomorrow: confirm those addresses are right for the client
build you actually run (Step 2). If the fingerprint guard trips (no feed, but the
DLL loads fine), the client build differs from the one the recipe targets and the
five site addresses + fingerprint bytes need re-deriving for your build -- that is
the only authoring case left, and it stays in this gitignored file (never commit
addresses).

The DLL was already rebuilt with the filled seam; if you edit the local header,
rebuild: `just build-posfeed-dll`.

## Step 2 -- live flight check (the actual CV-MVAS-POS verification)

1. `just play-local`, log in, undock.
2. Fly with **normal thrusters only** -- no autopilot, no warp.
3. You should travel and STAY where the client renders you. Pressing
   spacebar/stop, or starting autopilot/warp, must NOT snap you back to a stale
   position. (Pre-fix, it did -- that's the PB-2 symptom.)
4. Confirm you can move off a planet/station and warp normally.
5. Sanity-check the proxy: `0x1004` to `udp/3806` should appear only while you are
   actually thrusting, change-gated (not a constant flood when stationary).

## Step 3 -- close out

- [ ] Tick CV-MVAS-POS in `plans/29-client-verification.md` once flight stays in
      sync against the real client.
- [ ] Flip PB-2 `[~]` -> `[x]` in `plans/41-player-reported-bugs.md`.
- [ ] Mark task #105 (CV-MVAS-POS) complete.
- [ ] If the engine read needed anything the recipe didn't cover, note the delta
      here (method only, no addresses) so the next build bring-up is faster.

## If it does NOT stay in sync

The most likely failure is the position attribution / coordinate frame, not the
transport (the `0x1004` wire shape is already byte-pinned by
`CaptureReplayTests.MvasPosition_1004_*`). Check, in order: (a) the matrix offsets
(pos vs heading columns swapped), (b) the sector id field (feeding a wrong sector
makes the server reject or mis-place the sample), (c) the staleness/change gate in
`proxy/UDPClient_linux.cpp` dropping good samples. Capture the loopback `udp/3807`
datagrams and the proxy's `0x1004` output and diff against the live Combat capture
referenced in PB-2.
