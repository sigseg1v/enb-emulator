# Phase AN -- FreyaLauncher auto-update (patcher) + login `/updateCheck` + S3/CloudFront delivery

Status: **C1 + C2 IMPLEMENTED (code only). C3-C5 still owner-gated -- NO cloud
resources created.** The launcher updater (C1) and login `/updateCheck` endpoint
+ startup manifest hash cache (C2) are built, unit-tested, and verified
end-to-end against a local `file://` manifest stub (no cloud). The delivery
infra (C3 terraform: S3 + CloudFront + ACM + WAF + Route53), the push pipeline
(C4), and the installer switch (C5) remain owner-gated and unexecuted.
Owner directive 2026-06-07: "Plan this all out carefully into a planning doc
before executing it. Don't create any resources until I tell you to."

> **HARD GATE:** Do not run `just up` (deploy/do), `terraform apply`, create any
> S3 bucket / CloudFront distribution / ACM cert / WAF, or push artifacts to S3
> until the owner explicitly says go. This phase is design + (later, on the
> owner's word) code + (last, on the owner's word) infra.

---

## 1. Goal

Give the Windows build of **FreyaLauncher** a self-updater so players always run
the current launcher + proxy without a manual reinstall:

1. On launch, before the player can hit **Play**, the launcher hashes its own
   `FreyaLauncher.exe` and `FreyaProxy.exe` (SHA-512) and asks the login server
   `/updateCheck` whether they are current.
2. If current -> status flips to **Online**, Play enables.
3. If not -> prompt, download the new files from CloudFront, verify hashes,
   atomically replace (including replacing the running launcher), and
   auto-relaunch.

Local dev (`just play-local`) builds the launcher with the updater **compiled
out** (`checkForUpdates=false`) so it never phones home.

The login server learns the authoritative hashes from a **private S3 bucket**
served publicly through **CloudFront** (`dl.<server-domain>`), fronted by a
**WAF** download throttle, with an **ACM** cert. `just push` (DO) builds the
Windows artifacts, overwrites them in S3, and invalidates CloudFront.

---

## 2. Components (5)

| # | Component | Where | Builds locally? |
|---|---|---|---|
| C1 | Launcher updater (hash, prompt, download, verify, self-replace, relaunch) | `tools/LaunchFreya/` | yes |
| C2 | Login `/updateCheck` HTTPS endpoint + startup hash cache | `login-server/Net7SSL/` | yes |
| C3 | Delivery infra: private S3 + CloudFront + ACM + WAF + Route53 `dl.` record | `deploy/do/terraform/` | no (cloud) |
| C4 | `just push` pipeline: build Win artifacts -> S3 overwrite -> CF invalidation | `deploy/do/scripts/Build-And-Push.ps1` (+ a Win-artifact build) | partial |
| C5 | linux-installer: stop pulling the upstream Net-7 patcher; use FreyaLauncher | `client/linux-installer/` | n/a |

---

## 3. Protocol / wire-fidelity note (CLAUDE.md)

`/updateCheck` is a **new out-of-band HTTPS endpoint on the auth server**, a
sibling of `/AuthLogin` (routed at `login-server/Net7SSL/SSL_Connection.cpp:261`
by `strstr(recv_buffer, "/AuthLogin")`). It is **NOT** part of the EnB game
protocol and alters **no** byte the real EnB client ever parses -- the real
client never calls it; only our launcher does. Therefore the wire-fidelity gate
(CLI byte-pin + plans/29 CV for game-protocol changes) **does not apply** to the
endpoint's payload.

It IS a new **network attack surface** on the login server, so it gets its own
security review (section 7) and a real-launcher verification entry (section 8),
not a game-protocol CV. It must not weaken the existing `/AuthLogin` path or the
server's security posture.

---

## 4. The contract (request/response)

### `/updateCheck`  (launcher -> login, HTTPS POST, JSON)

Request body (launcher sends its two local hashes):
```json
{ "launcherHash": "<sha512 hex of FreyaLauncher.exe>",
  "proxyHash":    "<sha512 hex of FreyaProxy.exe>" }
```

Response -- up to date:
```json
{ "status": "UP_TO_DATE" }
```

Response -- update needed. The `files` list is built **conditionally** from
which of the two hashes mismatched:

```json
{ "status": "UPDATE_NEEDED",
  "files": [
    { "relativePath": "FreyaLauncher.exe", "url": "https://dl.<domain>/FreyaLauncher.exe", "hash": "<sha512>" },
    { "relativePath": "FreyaLauncher.cfg", "url": "https://dl.<domain>/FreyaLauncher.cfg", "hash": "<sha512>" },
    { "relativePath": "FreyaProxy.exe",    "url": "https://dl.<domain>/FreyaProxy.exe",    "hash": "<sha512>" }
  ] }
```

**Decision (owner, 2026-06-07):** only **two** hashes are ever checked --
`FreyaLauncher.exe` and `FreyaProxy.exe`. `FreyaLauncher.cfg` has **no
independent hash check**: it is *bound to the launcher*. So the server builds
`files` per mismatch:
- launcher EXE hash mismatches -> include **`FreyaLauncher.exe` + `FreyaLauncher.cfg`** (cfg always rides with a launcher update).
- proxy EXE hash mismatches -> include **`FreyaProxy.exe`**.
- both mismatch -> all three.

That is why the cfg needs no hash comparison server-side -- it ships whenever the
launcher does. The cfg *download* is still hash-verified by the launcher
(AN-C1-8) using the hash the server copied from the manifest; that hash is for
post-download integrity, not for the update decision.

---

## 5. Component detail + task list

### C1 -- Launcher updater  `tools/LaunchFreya/`

Hook point: the existing status-probe flow in `MainWindow.axaml.cs` (the
`c_ServerStatus` label + `_statusProbeGen` monotonic token, ~lines 275-305) and
the Play button enable/disable.

- [x] AN-C1-1 Compile-time flag `checkForUpdates`. Add an MSBuild property
      (e.g. `<CheckForUpdates>false</CheckForUpdates>` default) -> `DefineConstants`
      `CHECK_FOR_UPDATES`. `just play-local` / Debug = off; the Windows package
      build (task #76 `package-client-windows`) = on. When off, the whole
      updater path is `#if`'d out and status goes straight to the existing
      server-up probe.
- [x] AN-C1-2 SHA-512 of local `FreyaLauncher.exe` + `FreyaProxy.exe`
      (resolve next to the running launcher; proxy path = same dir `bin/` per
      `Launcher.cs`). Use `System.Security.Cryptography.SHA512`, lowercase hex.
- [x] AN-C1-3 POST to `https://<auth-host>/updateCheck`. Reuse the configured
      auth host/port the launcher already uses for the status probe. Short
      timeout; on transport failure show a clear non-fatal error and DO NOT
      enable Play (fail-closed: can't confirm currency -> can't play). (Decide
      with owner if a hard network failure should block or warn-and-allow;
      default here = block, matches "can't click play until this is done".)
- [x] AN-C1-4 Status-label state machine: `Checking...` while the request is in
      flight -> `Online` on UP_TO_DATE -> `Update Needed` on UPDATE_NEEDED
      (while prompting/downloading). Gate Play on reaching `Online`.
- [x] AN-C1-5 Prompt on UPDATE_NEEDED: "An update is available. Would you like
      to download? [OK] [Cancel]". Cancel -> exit the launcher. OK -> download.
- [x] AN-C1-6 **Path-traversal guard (SECURITY, mandatory).** For every
      `relativePath`: reject absolute paths, drive letters, and any path whose
      `Path.GetFullPath(Combine(baseDir, relativePath))` does not start with
      `baseDir + separator`. Reject `..` segments. baseDir = launcher install
      dir. A failing entry aborts the whole update (no partial application).
- [x] AN-C1-7 Download: delete `<baseDir>/updates/` if present, recreate it,
      fetch each file to `updates/<filename>`. Show a downloading spinner.
- [x] AN-C1-8 Verify: SHA-512 each downloaded file, compare to the response
      `hash`. If ANY mismatch -> abort, do not replace anything, surface error.
- [x] AN-C1-9 Atomic replace, **including the running launcher**. Windows can
      rename (MoveFile) a running image but not overwrite/delete it. Plan:
      - For non-running files (`FreyaProxy.exe`, `FreyaLauncher.cfg`): the proxy
        is not running at launch time, so move-replace directly.
      - For the running `FreyaLauncher.exe`: rename self -> `FreyaLauncher.exe.old`,
        move `updates/FreyaLauncher.exe` -> `FreyaLauncher.exe`, start the new
        exe, exit. The freshly-started new launcher deletes any `*.old` on
        startup. No external helper exe (OPEN-Q4 RESOLVED).
- [x] AN-C1-10 Auto-relaunch the new launcher after replace; old process exits.
- [x] AN-C1-11 Startup cleanup: delete leftover `FreyaLauncher.exe.old` and a
      stale `updates/` dir.
- [x] AN-C1-12 Unit-test the pure logic: path-traversal guard (reject `..`,
      absolute, sibling-escape), hash compare, response JSON parse.

### C2 -- Login `/updateCheck` endpoint + hash cache  `login-server/Net7SSL/`

- [x] AN-C2-1 Route `/updateCheck` next to `/AuthLogin`. NOTE: the real Linux
      request handler is `HandleHttpsRequest()` in `LinuxAuth.cpp`, not
      `SSL_Connection.cpp` -- that whole TU is `#ifdef WIN32`-walled and compiles
      to nothing on Linux. The route + `HandleUpdateCheck()` live in LinuxAuth's
      anonymous namespace; the POST body's two hex hashes are extracted with a
      bounds-checked `JsonField` (malformed -> "" -> mismatch, never a crash).
- [x] AN-C2-2 In-memory hash cache: the authoritative SHA-512 of the 3 published
      files. Populated **once at login-server startup** by a plain HTTPS GET of a
      small `manifest.json` over CloudFront (OPEN-Q1 RESOLVED -- manifest GET, no
      S3 HEAD, no AWS creds). **No TTL** -- the cache is refreshed by restarting
      the login server, which `just update` does (OPEN-Q2 RESOLVED). Thread-safe
      read on the request path. **Fetch mechanism:** the login server links
      OpenSSL but NOT libcurl today; add **libcurl** (Dockerfile
      `libcurl4-openssl-dev` build + `libcurl4` runtime; CMake link `CURL::libcurl`)
      and GET `NET7_PATCHER_MANIFEST_URL`. libcurl also reads `file://` URLs, so
      the **local-stub test (step 1) points the env at a manifest on disk** -- no
      TLS, no cloud. manifest.json schema: `{ "files": [{ "relativePath", "sha512" }, ...] }`.
- [x] AN-C2-3 Compare client `launcherHash` + `proxyHash` to cache. Both match
      -> `UP_TO_DATE`. Else -> `UPDATE_NEEDED` with a **conditional** `files`
      list: launcher mismatch adds `FreyaLauncher.exe` + `FreyaLauncher.cfg`
      (cfg always rides with the launcher -- it has no own hash check); proxy
      mismatch adds `FreyaProxy.exe`; both -> all three. URLs from config +
      cached hashes (the cfg hash comes from the same manifest, used only for
      the launcher's post-download integrity check, not the decision). Keep the
      compare simple and allocation-light.
- [x] AN-C2-4 Config: CF base URL (`dl.<domain>`) and the manifest URL come from
      env/config (NET7_ prefix per house style, e.g. `NET7_PATCHER_DL_BASE`,
      `NET7_PATCHER_MANIFEST_URL`). No secrets. No TTL knob (startup-only fetch).
- [x] AN-C2-5 Cache-miss = server DOWN (OPEN-Q5 RESOLVED, owner 2026-06-07). If
      the hash cache is not yet populated (first boot before the manifest GET
      completes, or the GET failed), the login server reports the **server status
      as DOWN** -- the launcher shows the server offline and Play stays disabled.
      Fail-closed: no update decision is made on an empty cache. The window is the
      brief startup gap before the manifest loads; `just update` re-triggers the
      fetch. (Implication: the manifest fetch must succeed for the server to read
      as UP -- treat a persistent manifest-GET failure as a deploy error, not a
      silent degrade.)
- [x] AN-C2-6 Never read arbitrary local files; never reflect client input into
      a path or shell. The endpoint only ever returns the 3 fixed entries.

### C3 -- Delivery infra (terraform, `deploy/do/terraform/`)  **owner-gated**

Existing infra already uses the `aws` provider for Route53 + the `acme` provider
(Let's Encrypt) for the **game** cert. CloudFront needs an **ACM** cert in
**us-east-1** specifically (CloudFront requirement, regardless of droplet region).

- [ ] AN-C3-1 New env vars in `deploy/do/.env.example`:
      `ENB_PATCHER_PRIVATE_S3_BUCKET` (globally-unique private bucket name),
      and a derived `dl.<DOMAIN_NAME>` (or an explicit `PATCHER_DL_DOMAIN`).
- [ ] AN-C3-2 Private S3 bucket (block all public access). Versioning optional
      (owner said don't worry about keeping old versions -- default off, but
      note in README the push/update race in section 6).
- [ ] AN-C3-3 CloudFront distribution serving the **private** bucket via **OAC**
      (Origin Access Control; bucket policy grants only the distribution).
- [ ] AN-C3-4 ACM cert for `dl.<domain>` in **us-east-1** (new `aws` provider
      alias `aws.us_east_1`), DNS-validated via the existing Route53 zone.
- [ ] AN-C3-5 Route53 record `dl.<domain>` -> the CloudFront distribution.
- [ ] AN-C3-6 WAF (WAFv2, scope=CLOUDFRONT, us-east-1) attached to the
      distribution -- **one per-IP rate-based rule** (OPEN-Q6 RESOLVED, owner
      2026-06-07). Aggregate by **source IP**, limit ~**20 requests / 5 min**
      (the WAF floor is 10; 20 leaves headroom for a legit 3-file update + a few
      retries while still tripping a spam loop). Action = block. No per-file
      granularity, no Lambda@Edge token-bucket -- the goal is "no surprise bill,"
      and a per-IP cap plus CloudFront caching (repeat downloads served from edge
      cache, not S3) achieves it simply. (Residual: a distributed many-IP flood
      defeats any per-IP rule; a global-constant second rate rule could be added
      later if ever needed, not day one.)
- [ ] AN-C3-7 Wire all of the above behind the SAME idempotent `terraform apply`
      that `deploy/do` `just up` runs (Deploy-Infra.ps1). **CONFIRMED (owner
      2026-06-07):** "run init again" = `deploy/do` `just up` (root `just init`
      is dev-only); fold the S3/CF/ACM/WAF/Route53 resources into that same apply
      so re-running idempotently converges after the operator fills the new env
      fields.

### C4 -- `just push` pipeline  `deploy/do/scripts/Build-And-Push.ps1` (+ Win build)

- [ ] AN-C4-1 Build the Windows artifacts: `FreyaLauncher.exe`
      (`dotnet publish` win-x64, `CheckForUpdates=true`), `FreyaProxy.exe`
      (MinGW Win32 cross-build, `proxy/cmake/mingw-w64-x86_64.toolchain.cmake`),
      and the packaged `FreyaLauncher.cfg`. Likely a repo-root
      `just package-client-windows` (task #76) that Build-And-Push calls.
- [ ] AN-C4-2 Compute SHA-512 of all three; write a `manifest.json`
      (relativePath + sha512 for the 3 files) -- this is the single hash source
      the login server GETs over CloudFront (OPEN-Q1 RESOLVED; no S3 HEAD, no
      object metadata path).
- [ ] AN-C4-3 Upload (overwrite) the 3 files + `manifest.json` to
      `s3://$ENB_PATCHER_PRIVATE_S3_BUCKET/`.
- [ ] AN-C4-4 Trigger CloudFront invalidation `/*` after upload.
- [ ] AN-C4-5 **Push -> update ordering (OPEN-Q2 RESOLVED).** The login server
      reads the manifest only at startup (no TTL), so a `just push` does NOT take
      effect until the login server restarts. Operator runs `just push` (build +
      S3 overwrite + CF invalidation) **then `just update`** (Update-Stack.ps1
      restarts the login container -> re-fetches the manifest). Document this
      two-step in the deploy README and ideally have `just push` print the
      reminder (or chain `just update`) at the end.
- [ ] AN-C4-6 Document that the **server-side** `just push` (server/login images)
      and this **client-artifact** push are distinct concerns; keep the proxy a
      client artifact (it is never deployed to the droplet -- main.tf already
      says so).

### C5 -- linux-installer  `client/linux-installer/`

- [ ] AN-C5-1 The installer currently downloads/patches the upstream Net-7
      `LaunchNet7.exe` patcher (referenced in docs as "stays"). That changes:
      point the installer at **our** `FreyaLauncher` (from CloudFront / the
      package) instead of the upstream patcher. Coordinate with Phase U
      (21-phase-u-linux-installer-fixes.md). Preserve the GPLv3 header/license.
- [ ] AN-C5-2 Update docs (docs/07, the installer README) to describe Freya's
      own updater replacing the upstream patcher flow.

---

## 6. The push/update race (documented, accepted by owner)

`just push` overwrites the S3 objects and invalidates CloudFront. There is no
versioning/coexistence window: a launcher that fetched the manifest just before
an overwrite could download a file whose bytes changed under it. The hash
verify (AN-C1-8) catches the mismatch and the update simply **aborts and retries
on next launch** -- it never applies an inconsistent set. Owner directive: do
not engineer around this; document it as a known small race. (Belt-and-braces:
the login server's TTL cache + CF invalidation make the window small.)

---

## 7. Security review (the new surface)

- **Path traversal** (AN-C1-6) is the highest-risk item: a compromised/spoofed
  `/updateCheck` response must never write outside the launcher dir or overwrite
  arbitrary files. Guard + abort-on-any-bad-entry is mandatory and unit-tested.
- **Hash verification** (AN-C1-8) before replace defends against a corrupted or
  MITM'd download. Combined with TLS on both `/updateCheck` and CloudFront.
- **TLS everywhere**: `/updateCheck` over the existing login TLS; downloads over
  CloudFront HTTPS (ACM cert). No plaintext.
- **`/updateCheck` is unauthenticated** (pre-login by design) and returns only
  public data (download URLs + public-binary hashes). It must remain a pure
  in-memory compare with bounded input -- no file reads, no DB, no reflection of
  client bytes into any path. Keep it cheap (DoS-resistant); rely on CF+WAF for
  download abuse and add a light per-IP cap on the endpoint if needed.
- **Self-replace** must not leave the launcher in a half-updated state: verify
  ALL hashes first, then swap; keep the `.old` until the new process confirms.
- **No secrets** in any committed file: bucket name and `dl.` domain are config
  (env), AWS creds stay in the gitignored operator `.env` / droplet IAM, S3
  write creds live only in the push environment.

---

## 8. Client/owner verification (this phase's CV entries -- plans/29)

These are real-launcher (not real-EnB-client, not game-protocol) checks:

- [ ] CV-AN-1 Windows `FreyaLauncher.exe` built with `CheckForUpdates=true`
      calls `/updateCheck`, shows `Checking...` -> `Online` when current.
- [ ] CV-AN-2 With a newer artifact published, the launcher prompts, downloads
      from `dl.<domain>`, verifies, replaces (incl. itself), and relaunches.
- [ ] CV-AN-3 Path-traversal guard rejects a crafted `relativePath` (manual or
      a local stub server) without writing outside the install dir.
- [ ] CV-AN-4 `just play-local` build has the updater compiled out (no
      `/updateCheck` call).

---

## 9. Open questions for the owner

All resolved 2026-06-07.

- **OPEN-Q1 (hash source) -- RESOLVED.** Don't HEAD the bucket. `just push`
  writes a small `manifest.json` (the 3 hashes) to the private bucket; the login
  server does a plain credential-free HTTPS **GET** of it over CloudFront at
  startup. Simplest CF-fetchable hash source; no S3 HEAD, no SigV4, no object
  metadata path. (AN-C2-2, AN-C4-2.)
- **OPEN-Q2 (cache freshness) -- RESOLVED.** No TTL. The login server fetches the
  manifest once at startup; `just update` restarts the login container to pick up
  new hashes. Operator workflow is `just push` then `just update`. (AN-C2-2,
  AN-C4-5.)
- **OPEN-Q3 (cfg response hash) -- RESOLVED.** The cfg has no update-decision
  hash; it is bound to the launcher and ships whenever the launcher EXE hash
  mismatches. Its `hash` in the response (for the launcher's post-download
  integrity check) comes from the same `manifest.json`, which carries all three.
  No cfg comparison server-side.
- **OPEN-Q4 (self-replace mechanism) -- RESOLVED (owner, 2026-06-07).** Use
  rename-self-then-relaunch, **no external helper**. (AN-C1-9.)
- **OPEN-Q5 (login cache-miss behavior) -- RESOLVED.** Cache not yet populated ->
  the login server reports the **server status as DOWN** (fail-closed); the
  launcher shows offline and Play stays disabled until the manifest loads.
  (AN-C2-5.)
- **OPEN-Q6 (WAF granularity) -- RESOLVED.** No edge-function token bucket. One
  WAFv2 per-IP rate-based rule, aggregate by source IP, limit ~20 req / 5 min
  (floor is 10), action block. Lets a legit 3-file update + retries through;
  trips a spam loop. CloudFront caching keeps S3/egress cost low. (AN-C3-6.)
- **OPEN-Q7 (`just init` vs `just up`) -- RESOLVED.** "Run init again" = the
  `deploy/do` idempotent terraform converge `just up`; the S3/CF/ACM/WAF/Route53
  resources fold into that same apply. (AN-C3-7.)

---

## 10. Suggested execution order (once owner says go)

1. C1 + C2 against a **local stub** (a throwaway local HTTPS endpoint serving a
   hand-written manifest + files from disk) -- proves the launcher flow and the
   login endpoint end-to-end with zero cloud spend.
2. C4 build half (produce the Win artifacts + manifest locally) -- still no cloud.
3. **PAUSE for owner go-ahead on infra**, then C3 (terraform) + C4 upload/
   invalidation, then C5 installer.
4. Owner runs CV-AN-1..4 against the real CloudFront delivery.

> Reminder: steps 3-4 create billable AWS resources (S3, CloudFront, ACM, WAF,
> Route53) and must wait for the explicit owner go-ahead.
