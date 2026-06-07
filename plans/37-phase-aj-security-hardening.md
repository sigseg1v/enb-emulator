# Phase AJ -- server-side auth & input-validation hardening (security audit)

**Status: in progress (2026-06-07).** Internal security analysis -- do NOT copy
the threat/vulnerability content into `docs/` or commit messages as a disclosure.
The public docs get the *resulting secure design* only, after the fixes ship
(see task #88). This file is the working findings + fix tracker. It pairs with
`plans/35` (Phase AH: DTLS + C->S per-packet token), which is the transport/auth
layer; this phase is the per-handler input-validation and authorization layer.

Source: aggressive read-through + three parallel adversarial Explore passes over
`server/src`, `login-server/`, `proxy/` on 2026-06-07. Each finding is tagged
**CONFIRMED** (I read the exact code) or **REPORTED** (audit-flagged, needs a
direct re-read before fixing) and **LIVE** (compiles on the Linux build) or
**DEAD** (Win32-walled / not in the CMake target).

## Done this turn

- [x] **AJ-0a. CSPRNG ticket -- login-server.** `login-server/Net7SSL/LinuxAuth.cpp`
  `BuildTicketLocked`: `rand()` -> libsodium `randombytes_buf` 16B -> 32 hex.
  CONFIRMED + fixed.
- [x] **AJ-0b. CSPRNG ticket -- game server.** `server/src/AccountManager.cpp`
  `IssueTicket` was a SECOND live `rand()` ticket generator (line 846-849) that
  the login-server fix did not cover. Same fix applied (libsodium). CONFIRMED +
  fixed. NOTE: still useless for auth until AJ-1 lands -- see below.

## CRITICAL -- confirmed, open

- [ ] **AJ-1. Game server never validates the ticket suffix.** CONFIRMED / LIVE.
  `server/src/UDP_Global.cpp` `ProcessTicketInfo` (114-176): splits the ticket on
  `-` via `strtok_s`, takes the username, checks account exists / not banned /
  not in use, then `SendAvatarList`. The random suffix is compared against
  **nothing**. The Win32 ticket store check (`GetUsernameFromTicket`,
  `AccountManager.cpp` ~874-905) is commented out. Consequence: any host that can
  reach the global UDP port can present `victim_username-anything` and be served
  that account (subject only to the account-in-use check) -- no password, no
  sniffing, no shared NAT. This is the headline hole. Fix = bind the issued/
  presented token to the account and verify it (see AH-9 stateless-signed-token
  option to avoid a cross-process store). This is a **tightening** (restores the
  dropped Win32 validation) -- always-welcome under the server-integrity rules.

- [ ] **AJ-2. `HandleSkillAction` out-of-bounds array index.** CONFIRMED / LIVE.
  `server/src/PlayerSkills.cpp:97-138`: `SkillAction.SkillID` (a wire-controlled
  `short`, `common/include/net7/PacketStructures.h:1001`) indexes
  `RPGInfo.Skills.Skill[Action->SkillID]` with NO bounds check before first use
  (line 101), and is then used to SetLevel/SaveNewSkillLevel. The array is
  `_Skill Skill[170]` (`server/src/AuxClasses/AuxSkills.h:25`). A `SkillID` of e.g.
  20000 reads/writes far out of bounds -> memory corruption, potential RCE or
  cross-object overwrite (skill points, adjacent player state). Fix: reject
  `SkillID < 0 || SkillID >= 170` (and ideally `>= 64`, the count of real skills)
  with a single rate-limited warn + return. Pure tightening.

## HIGH / CRITICAL -- reported, need direct re-read before fixing

- [ ] **AJ-3. `HandleInventoryMove` slot indices unbounded.** REPORTED / LIVE.
  `server/src/PlayerConnection.cpp` ~2510-3100 (opcode 0x0027): audit flags
  `FromSlot`/`ToSlot` (wire `ntohl`) used to index `CargoInv.Item[]` (size 40),
  ammo (20), equip (9), vault (40), trade (6) with no bounds check, on the
  `GetData()/SetData()` paths. If true this is OOB read+write -> heap corruption /
  item dupe / cross-player corruption. RE-READ the handler and confirm there is no
  earlier guard before adding per-container bounds checks with the REAL slot
  constants (do not hardcode the audit's guessed numbers -- read the Aux*Inventory
  headers).

- [ ] **AJ-4. Chat sender GameID spoofing.** REPORTED / LIVE.
  `server/src/PlayerConnection.cpp` ~4642/4648/4654 (opcode 0x0033, Type 2/3/4 =
  guild/local/broadcast): passes `chat->GameID` (from the wire) as the sender to
  `GuildChat/LocalChat/BroadcastChat` instead of `this->GameID()`. GroupChat
  (~4635) correctly uses `GameID()`. If confirmed, an authenticated player can
  emit chat attributed to any other GameID -- exactly the owner's "spoof them to
  chat" scenario. Fix: ignore the wire GameID, always use `GameID()` (or reject on
  mismatch with a warn). Pure tightening. This one is high-value and low-risk;
  prioritize after re-read.

- [ ] **AJ-5. Character CREATE/DELETE not bound to an authenticated session.**
  REPORTED / LIVE. `server/src/UDP_Global.cpp` create (~254) / delete (~302)
  handlers take the target account/username from the wire packet, not from a
  verified session. Paired TODO at `UDP_Global.cpp:98-99`: "we need a list of
  accounts which are logged in successfully, so Net7Proxy create/delete can't be
  abused." Trace whether reaching the global UDP port lets an attacker create/
  delete characters on an arbitrary account without auth. If so: gate on the
  AJ-1 token binding. CONFIRM the data flow before claiming.

## MEDIUM / LOW -- reported

- [ ] **AJ-6. No ticket-expiry enforcement on the game server.** REPORTED / LIVE.
  `ProcessTicketInfo` never checks expiry; the Win32 expiry check is commented
  out. Fold into AJ-1 (validate token + expiry together).
- [ ] **AJ-7. Account-in-use check is a racy list scan.** REPORTED / LIVE.
  `PlayerManager::CheckAccountInUse` -- check-then-act gap between the scan and
  `SetupPlayer` insertion; also force-logs-out the existing session, which is a
  griefing lever (repeated logins kick a victim). Re-read; decide if it needs an
  atomic guard.
- [ ] **AJ-8. Missing banned-IP checks.** CONFIRMED present as TODOs / LIVE.
  `UDP_Global.cpp:98`, `ClientToGlobalServer.cpp:418`, `SSL_Connection.cpp:456`.
  No IP ban/rate-limit on the UDP or auth paths. Lower priority than auth holes
  but enables brute-force / flood. Consider a rate-limit once AJ-1 lands.
- [ ] **AJ-9. `HandleStarbaseRequest` trusts wire StarbaseID without a
  location check.** REPORTED / LIVE. `PlayerConnection.cpp` ~9871-9945 (0x004E):
  may let a docked player interact with NPCs/vendors at a station they are not at.
  Re-read; likely a game-logic check, fold into normal hardening.

## Dead / non-issues (recorded so they aren't re-flagged)

- **`login-server/Net7Mysql/Tab2.cpp:118,284` SQL injection via sprintf.** DEAD
  (Win32 MFC admin tool, `#ifdef WIN32`, not in the Linux build). Still violates
  the no-string-concat-SQL rule -- if this tool is ever revived it must be
  parameterized; otherwise a candidate for deletion (no-dead-code rule).
- **`rand()` in MOB spawn / loot / field gen** (`MOBClass.cpp`, `FieldClass.cpp`,
  etc.) -- non-security gameplay randomness, intentional, leave as-is.
- **Default DB creds `net7/net7` in `docker-compose.yml`** -- local-dev defaults,
  allowed by CLAUDE.md for the loopback stack; the public deploy MUST override via
  env/secrets. Verify the deploy does not inherit these.
- **Westwood RSA-512 / RC4** -- legacy client-compat crypto, required by the
  retail client, not a finding.

## Process notes

- AJ-1..AJ-5 change packet ACCEPTANCE on the server. Per CLAUDE.md server-integrity
  rules they are **tightenings** (rejecting input the real server/client never
  produced), which are always-welcome with a primary-source citation -- but each
  still needs a CLI parse/test that exercises the rejected-vs-accepted boundary
  and a `plans/29` CV entry before it is DONE. Do them one at a time with the test,
  not in a big unverified batch.
- Re-read every REPORTED item against the live source before writing a fix. The
  audit agents over-report memory-corruption; AJ-2 was verified by hand and is
  real, but AJ-3/AJ-9 must be confirmed the same way.

---

# Phase AK -- adversarial final security audit (do LAST)

**Status: NOT STARTED. Runs only after Phase AH (DTLS + token) and Phase AI
(input-validation hardening) are both complete and verified.** This is the
closing red-team pass the owner asked for: assume everything above is "fixed"
and try to break it anyway.

- [ ] **AK-1. Re-audit the now-hardened auth path adversarially.** Fan out
  independent adversarial reviewers (no knowledge of the fixes' intent) over:
  ticket issue/validate, DTLS handshake + pin verification, the C->S token
  envelope (replay across sessions? token reuse after logout? token for account A
  accepted on account B's session? truncated/oversized token?), create/delete,
  and the per-packet GameID/ownership checks. Each finding gets adversarially
  verified (>=majority of independent reviewers must agree it is real) before it
  is logged, to avoid the over-report noise.
- [ ] **AK-2. Fuzz the wire boundaries.** Malformed/oversized/truncated frames at
  every externally-reachable UDP listener and the proxy's client-facing TCP:
  sub-packet demux loops, split-packet reassembly (`m_SplitPacketBuffer`),
  slot-index arrays (`m_PacketSlots[SLOT_RANGE]`), every `packet[index]` walk and
  attacker-length `memcpy`. Confirm no crash / OOB.
- [ ] **AK-3. Re-sweep for `rand()`/weak-RNG, `strtok`-trust, and "not ported"/
  TODO security holes** that the first pass missed or that new code introduced.
- [ ] **AK-4. Verify the DTLS/token OFF-for-tests gate cannot be flipped on in
  prod** and that the public deploy does not ship cleartext or default creds.
- [ ] **AK-5. Sign-off note** in `plans/99-decisions-log.md` recording the
  audit scope, what was tested, and residual accepted risk.
