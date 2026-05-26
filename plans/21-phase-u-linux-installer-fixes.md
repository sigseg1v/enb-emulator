# Phase U — Linux client-installer fixes (optional, late-stage)

**Status: in progress (2026-05-26). User approved local-fork direction
on 2026-05-26 with the directive "any fixes we do, modify our copy of
the script locally". Wave 1 (LaunchNet7 TLS) landed; C&S Creator rc 71
(items U.1–U.5 in their original framing) still open.**

## Why this phase exists

`client/linux-installer/install-enb-linux.sh` is vendored verbatim from
upstream (`ciphersimian/enb-linux-installer`, GPLv3). The README pins the
last-known-good wine version at **wine-9.20 on Manjaro (2024-12-15)**.

On 2026-05-26 a user run on Ubuntu with **wine-11.8** failed at the
**Character and Starship Creator** step:

```
2026-05-26 08:36:18 sigsegv-box: >> ERROR: rc: 71, output:
```

The failure is reproducible. The earlier `e&bsetup.exe` InstallShield
step (Earth & Beyond client itself) on the same wine 11.8 prefix
succeeded, so the issue is specific to the C&S Creator silent install
script — most likely a stale setup.iss GUID against a newer
`CharacterStarshipCreator.exe`, or wine 11.x dropping support for the
2002-era InstallShield 6/7 runtime the C&S installer ships.

## Hard constraints (read before touching anything)

1. **The script is GPLv3.** Modifying our local copy means we are now
   distributing a *modified* GPLv3 work. That obliges us to:
   - Add a header note documenting the modifications and date
     (GPLv3 §5(a)).
   - Keep the upstream `LICENSE` file intact next to the script
     (already in place at `client/linux-installer/LICENSE`).
   - Keep the script's project-default license (CC BY-NC-SA) carve-out
     respected — per-folder LICENSE wins per CLAUDE.md license rules.
2. **Upstream divergence is a tax.** Any local fork starts accruing
   merge debt against `ciphersimian/enb-linux-installer`. Before forking,
   prefer the path-of-least-divergence:
   - First attempt: send the fix upstream as a PR. If accepted, our
     `client/linux-installer/` re-syncs from upstream and the local
     divergence disappears.
   - If upstream is unresponsive or rejects, *then* fork locally with
     a documented modification block at the top of the script.
3. **CLAUDE.md rule reminder**: `client/**` is allowed to use Win32
   APIs and WINE-specific behaviour. The "no Win32 in server-native
   code" rule does NOT apply here — the installer drives Win32 binaries
   under wine by design.

## Items

- [ ] **U.1 — Reproduce and capture the real error.**
  Re-run the C&S step in isolation with InstallShield logging enabled:
  ```sh
  WINEPREFIX=~/.wine-enb wine start /wait \
    "$(find /tmp -name CharacterStarshipCreator.exe 2>/dev/null | head -1)" \
    /s /sms /debuglog \
    /f1"$(dirname $(find /tmp -name CharacterStarshipCreator.exe 2>/dev/null | head -1))/setup.iss" \
    /f2"$HOME/csc-setup.log"
  ```
  Capture `~/csc-setup.log` and any `setup.log` the installer writes
  next to the EXE. Attach to the commit message that closes this item.

- [ ] **U.2 — Classify the failure mode.**
  Three likely candidates, ordered by prior probability:
  1. **Stale GUID** — the recorded setup.iss has GUID
     `{17FF7B21-A872-429C-9331-5883ACD12EE8}` (install-enb-linux.sh:793).
     If the C&S installer was rebuilt with a new GUID, silent install
     rejects the .iss. Verify by extracting the current installer's
     project GUID with `wine isscript32` or by running once interactively.
  2. **Wine 11.x ABI break** — wine 11 dropped or changed something
     InstallShield 6/7 (vintage 2002) relies on. Verify by running the
     EXE under wine 9.x in a separate prefix.
  3. **Missing prerequisite** — wine-gecko didn't install on this Ubuntu
     box (visible in the user's log). The C&S installer may invoke an
     IE/HTML control during its launch sequence.

  Don't skip U.2 — fixing the wrong root cause adds noise.

- [ ] **U.3 — Pick a fix strategy based on U.2.**
  - If U.2 == GUID drift: re-record `setup.iss` against the current EXE
    using `wine setup.exe /r` and embed the new GUID in the heredoc.
  - If U.2 == wine ABI: add a wine-version preflight in
    `check_wine_version()` that warns at >9.x and exits with a clear
    message on >10.x. Document the supported range.
  - If U.2 == gecko: harden the gecko install — fall back to manually
    downloading `wine-gecko-2.47.4-x86.msi` and `wine msiexec /i`-ing it.

- [ ] **U.4 — Decide: upstream PR vs local fork.**
  - Open an issue at github.com/ciphersimian/enb-linux-installer with
    the U.1/U.2 reproducer.
  - If upstream is active (commits in last 6 months), wait for response
    before forking locally.
  - If forking locally:
    - Add a `LOCAL_MODIFICATIONS.md` next to the script listing each
      diff against upstream with date + rationale (GPLv3 §5(a)).
    - Add a comment block at the top of `install-enb-linux.sh` stating
      "Modified from upstream {sha}; see LOCAL_MODIFICATIONS.md".
    - Pin the upstream sha we forked from in `LOCAL_MODIFICATIONS.md`
      so re-sync attempts have a base.

- [ ] **U.5 — Verify the fix end-to-end.**
  - Fresh wine prefix (rm -rf ~/.wine-enb).
  - Run the modified `install-enb-linux.sh` end-to-end on the
    failing-version target (Ubuntu + wine 11.8) and a known-good
    target (Manjaro + wine 9.20) to make sure we didn't regress the
    happy path.
  - C&S Creator launches and shows the avatar UI.

- [ ] **U.6 — Document the supported wine matrix.**
  Update `client/linux-installer/README.md` (which is part of the
  vendored GPLv3 work, so same modification rules apply) with:
  - Last-tested wine versions per distro.
  - Known-broken wine versions and why.
  - Instructions for pinning wine via winehq's apt repo if the
    distro ships a too-new version.

## Out of scope

- Rewriting the installer in non-bash. Upstream is bash; we stay bash.
- Supporting macOS. Upstream explicitly excludes Darwin (README §
  "Limitations").
- Migrating the C&S Creator away from InstallShield. The installer is
  Net-7 Entertainment's binary; we don't own it and can't repackage it.
- Replacing wine with Proton. Out of scope for this phase — the
  upstream script is wine-prefix-based and that's what we vendor.

## Wave 1 — LaunchNet7 cannot connect to update server (2026-05-26)

This wave was driven by a real reproducer during the same session
that opened the phase, ahead of the C&S Creator items above. It
landed before any of U.1–U.6 in the original ordering.

- [x] **W1.1 — Reproduce the patcher failure.** LaunchNet7.exe shows
  "Error: 2 / Couldn't connect to the update server. Try again
  later." on Ubuntu + wine-11.8 right after the Net-7 Unified
  Installer step. Screenshotted by user 2026-05-26.

- [x] **W1.2 — Classify root cause.** Network-side: `patch.net-7.org`
  serves a Let's Encrypt cert, 301-redirects HTTP -> HTTPS, accepts
  only TLS 1.2+ (verified via `curl` + `openssl s_client` from host).
  Client-side: LaunchNet7.exe targets .NET 2.0/3.5, whose
  `ServicePointManager.SecurityProtocol` defaults to TLS 1.0 only
  — explains why the HTTPS handshake fails even though host
  `curl https://...` succeeds.

- [x] **W1.3 — Apply Microsoft's documented workaround.** Set
  `SchUseStrongCrypto=1` + `SystemDefaultTlsVersions=1` under both
  `HKLM\Software\Microsoft\.NETFramework\v2.0.50727` and `v4.0.30319`,
  native and Wow6432Node. Verified live in the user's prefix —
  launcher proceeded past "Downloading Patch Information…".

- [x] **W1.4 — Decision: local fork.** Per user directive 2026-05-26.
  Earlier U.4 framing preferred upstream-PR-first; user overrode for
  speed. PR-back-to-upstream remains a follow-up but is not gating.

- [x] **W1.5 — Bake fix into our local script.** Three edits to
  `client/linux-installer/install-enb-linux.sh`:
  1. Header note documenting the fork (GPLv3 §5(a)).
  2. New post-N7-installer section that `sed`s LaunchNet7.cfg from
     `http://patch.net-7.org` to `https://patch.net-7.org`,
     idempotent.
  3. `winecfg -v win7` after the Net-7 installer (the existing
     `winetricks winxp` for `vcrun2008`/`dotnet20` stays — they
     need XP mode during install, but LaunchNet7 runs better on
     win7).
  4. Four `[HKLM\Software\...\Microsoft\.NETFramework\...]` blocks
     appended to the existing `REGISTRY_CONFIG` heredoc.
  Plus a new `client/linux-installer/LOCAL_MODIFICATIONS.md` per
  GPLv3 §5(a) listing the diff.

- [ ] **W1.6 — Re-sync verify.** Wipe `~/.wine-enb` and re-run the
  modified script end-to-end on Ubuntu + wine-11.8 to confirm the
  launcher connects on a fresh install (not just on the live prefix
  where I poked the registry by hand). Open until reproducible.

- [ ] **W1.7 — Upstream the patch.** Optional follow-up. Open an
  issue / PR at github.com/ciphersimian/enb-linux-installer with the
  reproducer + fix. If accepted, drop the local-mods section from
  `LOCAL_MODIFICATIONS.md` on the next sync.

## Reference

- Failing run: 2026-05-26 user session, rc 71 from
  `CharacterStarshipCreator.exe /s /sms` under wine-11.8 on Ubuntu.
- Script: `client/linux-installer/install-enb-linux.sh` lines 768-824.
- Upstream: github.com/ciphersimian/enb-linux-installer (GPLv3).
- License rules: `CLAUDE.md` § "License rules" + GPLv3 §5(a).
