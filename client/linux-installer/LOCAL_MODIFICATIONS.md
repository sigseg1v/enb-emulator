# LOCAL_MODIFICATIONS.md

This directory contains a **modified** copy of the upstream
`ciphersimian/enb-linux-installer` script. Upstream is GPLv3; per
§5(a), modifications must be documented and dated. This file is that
record.

## Upstream

- Source: <https://github.com/ciphersimian/enb-linux-installer>
- License: GPL-3.0 (see `LICENSE` next to this file — verbatim
  upstream copy, do not strip).
- Vendored into this repo on **2026-05-22** as part of Phase A
  (`plans/01-phase-a-merge.md`).

## Why a local fork

The enb-emulator project depends on this installer producing a
working wine prefix + EnB client + Net-7 launcher on contributors'
machines. When upstream regresses against newer wine versions, or
against changes in the live net-7.org infrastructure, we patch
locally so contributors aren't blocked. The long-term goal is to
upstream every patch back; when that lands the local diff drops
out of this file.

The full multi-phase plan is in
`plans/21-phase-u-linux-installer-fixes.md`.

## Modifications

### 2026-05-26 — Patch LaunchNet7 to connect to patch.net-7.org over modern TLS

**Symptom:** LaunchNet7.exe (the Net-7 patcher GUI) sits on
"Downloading Patch Information…" then surfaces an `Error: 2 /
Couldn't connect to the update server. Try again later.` dialog.
Reproducible on Ubuntu + wine-11.8 (the upstream script's
last-tested matrix is Manjaro + wine-9.20).

**Root cause:** Two-part wire problem. Patch server
`patch.net-7.org` now (1) serves Let's Encrypt TLS and 301-redirects
HTTP -> HTTPS, and (2) rejects TLS 1.0/1.1, accepting only TLS 1.2+.
LaunchNet7.exe is a .NET 2.0/3.5 WinForms binary whose
`ServicePointManager.SecurityProtocol` defaults to TLS 1.0 only,
and which doesn't follow the 301 redirect reliably under wine.

**Fix (three pieces, all in `install-enb-linux.sh`):**

1. **Post-installer `sed -i` over `LaunchNet7.cfg`.** Rewrites the
   four `baseUrl` / `fileListUrl` / `filesBaseUrl` / `versionFileUrl`
   plus `defaultWebsite` from `http://patch.net-7.org` to
   `https://patch.net-7.org`. Idempotent (grep guard).
2. **Two .NET reg keys per runtime, per arch.** Adds
   `SchUseStrongCrypto=1` and `SystemDefaultTlsVersions=1` under
   both `HKLM\Software\Microsoft\.NETFramework\v2.0.50727` and
   `HKLM\Software\Microsoft\.NETFramework\v4.0.30319`, native and
   Wow6432Node. These are Microsoft's documented workaround for the
   .NET 2.0/3.5 TLS-1.0-default issue.
3. **`winecfg -v win7` after the Net-7 installer.** The script's
   earlier `winetricks winxp` call is required by `vcrun2008` /
   `dotnet20` and stays as-is. Bumping to win7 *after* the launcher
   is installed improves LaunchNet7 compatibility on wine 10/11
   without disturbing the EnB client install path.

**Insertion points in upstream:**
- New `*** LOCAL MOD ***` block between the Net-7 Unified Installer
  section and the Character and Starship Creator section (upstream
  has `banner 'Character and Starship Creator Install'` on the
  immediately following line; the local block precedes it).
- New `*** LOCAL MOD ***` block appended to the existing
  `REGISTRY_CONFIG` heredoc, right before the closing `EOF`.

**Header note:** Top-of-file comment block (right after the
`#!/bin/sh` shebang) now documents this fork per GPLv3 §5(a) and
points to this file.

**Verified on:** Ubuntu (rolling) + wine-11.8 +
patch.net-7.org Version.txt = `545` (2026-05-26).

**Not yet upstreamed:** As of this commit the patch lives only in
the enb-emulator local copy. Phase U.4 (per
`plans/21-phase-u-linux-installer-fixes.md`) tracks the
upstream-PR-vs-local-fork decision; the user chose **local fork
first** on 2026-05-26.

## How to re-sync from upstream

1. `cd client/linux-installer && git log` — find the last sync sha.
2. Diff this directory against upstream's `master` at that sha.
3. Either:
   - merge upstream changes in, then re-apply each local mod block
     above (each is fenced with `*** LOCAL MOD ***` markers for
     greppability); or
   - if upstream has accepted any of our PRs, drop the corresponding
     section from this file.
4. Update the "Vendored into this repo on" line if a full re-sync
   happened.

## Conventions for adding new mods

- Each modification gets a dated section under `## Modifications`
  above (newest first).
- Wrap the code block with `*** LOCAL MOD ***` markers in the
  script so `git grep -n 'LOCAL MOD'` finds every change.
- Cite the symptom, root cause, and fix mechanism. Future
  maintainers (or upstream reviewers) need to recreate the diagnosis.
- If the change touches network endpoints or wine/winetricks verbs,
  note the wine version + distro it was verified on.
