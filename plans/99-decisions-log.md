# Decisions log (append-only)

Format: `## YYYY-MM-DD — short title`, then the decision, the alternatives considered, and why.

## 2026-05-22 — Project default license is CC BY-NC-SA 3.0, not GPLv3

Net-7's server code carries CC BY-NC-SA 3.0 headers in every source file. CC BY-NC-SA 3.0 is not GPL-compatible (NC clause), so a combined work can't be redistributed as pure GPLv3. Since the majority of code is Net-7's, we adopt CC BY-NC-SA 3.0 as the project default (`LICENSES/enb-emulator` overwritten from GPLv3 → CC BY-NC-SA 3.0). Per-file headers and per-folder LICENSE files take precedence, so the GPLv3 `client/linux-installer/LICENSE` still governs that script. **Practical implication: non-commercial only.**

Alternatives considered: keep GPLv3 for new code only (rejected as confusing); dual-license (rejected as legally noisy).

## 2026-05-22 — Use tada-o as the server base, not kyp

tada-o is svn r2974 from 2010 (~162K LOC); kyp is an older 2014 snapshot (~133K LOC, no svn metadata) with 20+ stub abilities tada-o implements. We use tada-o for `server/` and pull in kyp's `Net7Tools/`, `Documents/`, `capturedPackets/` which tada-o lacks.

## 2026-05-22 — C# tools target .NET 10, not Avalonia

Per user direction: upgrade `.csproj` files to SDK-style `net10.0-windows` with `<UseWindowsForms>true</UseWindowsForms>`. WinForms builds cross-platform via `dotnet build` but only runs on Windows. Avalonia port deferred to "open" item in Phase D notes.

## 2026-05-22 — Plans are committed to repo at ./plans/*.md

Per user direction: multi-phase work tracked in `./plans/*.md` (not in `~/.claude/plans/`). CLAUDE.md instructs every future agent to read them on startup, update them as work progresses, and not stop at phase boundaries.

## 2026-05-22 — Iteration does not stop at phase boundaries

Per user direction (explicit, repeated): a single invocation pushes through Phase A → B → C → ... continuously. Stops only on context exhaustion, unrecoverable external block, or all phases done. The user re-invokes as needed.
