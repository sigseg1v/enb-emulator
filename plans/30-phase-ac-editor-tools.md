# Phase AC -- Editor-tool usability: Postgres backend, zero-friction login, change-tracking, never-hang UI

Status: in progress (planning landed; implementation follows)

## Why this phase exists

The Avalonia editor suite (Sector / Mob / Mission / ... and the not-yet-ported
Item Editor) is currently close to unusable against the live stack, for several
compounding reasons surfaced while the owner tried to use the Sector Editor:

1. **The editors edit a DB the server never reads.** `commontools-avalonia`'s
   DB layer (`Database/DB.cs`) is `MySql.Data.MySqlClient`-based and
   `Gui/Login.axaml.cs` connects via `LoginData.ConnStr(...)` to host
   `net-7.org` / port `3307` -- the `mysql-legacy` container. But since Phase N
   the **live server reads Postgres (5432)**. So any edit made in an editor
   lands in a legacy MySQL DB that nothing runtime consults. The editors are
   writing to a dead DB.

2. **The login dialog is hostile.** It asks for Username / Password / SQL Host /
   SQL Port with defaults pointing at the wrong (MySQL) server. When the tools
   are launched via `just`, the local stack's connection details are already
   known -- the user should be able to just click Login (or skip it entirely),
   not hand-type connection params.

3. **No record of what changed.** There is no way to know which DB rows an
   editing session touched, and no way to produce a reviewable/replayable
   artifact of those changes (the owner explicitly asked for a generated `.sql`
   changeset).

4. **The UI hangs and can't be closed.** The tools get stuck on
   `Error within DB.executeQuery()` and the window X button does NOT close them
   -- the UI hangs. The X button must ALWAYS close the window, no matter what
   the DB layer is doing.

5. **The Item Editor is the lone un-ported editor.** `tools/itemeditor` is still
   WinForms-only; every other editor has a `tools/<name>-avalonia` port. It
   needs the same Avalonia treatment, and it must inherit all of the above
   fixes (it can't be ported onto the broken MySQL/login/no-tracking base).

6. **The Login window is too small.** The owner's screenshot showed a cramped
   ~290x195 dialog.

## Owner's words (verbatim anchors)

- "how do these editor tools even work? If I click Sector Editor it says 'Login:
  Username, Password, SQL Host, SQL Port' what is that trash. Just connect to the
  locally running db if we are running these with 'just', they should all be
  auto-populated in that case so I can just click login. Also how am I gonna know
  what changed? Can it record the records on the db that changed and generate a
  .sql file? How is that gonna work? Start with sector editor but it would need
  to apply to all. Also Sector Editor login is too small"
- "also can we port Item Editor (it says 'not yet ported')"
- "the tools all get stuck on a 'Error within DB.executeQuery() and they cannot be
  closed with the X button. The X button should always close and not hang the UI.
  add this as part of the phase"

## Scope / non-scope

In scope: the Avalonia editor suite under `tools/*-avalonia` and the shared
`tools/commontools-avalonia`. The legacy WinForms editors stay as-is (reference);
we are not reviving them.

Out of scope / NON-NEGOTIABLE guardrails:

- **This is a TOOLING phase. It does NOT touch `server/`, `proxy/`, or
  `login-server/`.** No server wire-behaviour change, no schema change to the
  live DBs for tool convenience. The editors adapt to the DB the server already
  uses, not the reverse (CLAUDE.md server-integrity rules).
- **Two-DB topology is real.** Content lives in Postgres DB `net7`; save-state
  lives in `net7_user`. Editors edit *content* -> they target `net7`. Do not
  cross-connection-join the two DBs (see the two-DB-topology memory). Confirm
  which DB each editor's tables live in before pointing it anywhere.
- **Postgres identifier folding.** Mixed-case identifiers (`npc_Id`) fold to
  lowercase unless quoted. The MySQL-era SQL in these editors is full of
  unquoted mixed-case names; porting to Npgsql will surface this. Quote
  identifiers or expect silent column-not-found.
- **No new MySQL-isms.** New/edited SQL targets Postgres syntax.

## Tasks

### AC.1 -- Migrate `commontools-avalonia` DB layer MySQL -> Postgres (Npgsql)

- [ ] Replace `MySql.Data.MySqlClient` usage in
      `tools/commontools-avalonia/Database/DB.cs` with `Npgsql`
      (`NpgsqlConnection`/`NpgsqlDataAdapter`/`NpgsqlCommand`/`NpgsqlTransaction`).
- [ ] Rewrite `LoginData.ConnStr(...)` to emit a Npgsql connection string
      (Host/Port/Database/Username/Password/...) for the `net7` content DB.
- [ ] Audit every SQL string the common layer issues for MySQL-isms (backticks,
      `LIMIT x,y`, mixed-case unquoted identifiers) and fix to Postgres syntax /
      quoting. Grep with `-a` (CRLF/non-ASCII trap).
- [ ] Decide the per-editor target DB explicitly (`net7` for content). Document.

### AC.2 -- Zero-friction login when launched via `just`

- [ ] When a tool is launched through `just` against the local stack,
      auto-populate the connection from the known local stack
      (host=localhost, port=5432, db=net7, the dev credentials the compose
      stack uses) so the user can click Login with no typing -- or skip the
      dialog entirely. Mechanism (env var the `just` recipe sets, a generated
      local config, or a "local stack detected" prefill) to be chosen in
      implementation; prefer an env var the recipe exports so there is no
      committed credential file.
- [ ] Keep a manual-entry path for non-local use, but it must NOT be the
      default friction for the common (local) case.
- [ ] Enlarge the Login window (the ~290x195 dialog is too small); make it
      resizable.

### AC.3 -- Change-tracking -> generated `.sql` changeset

- [ ] Record which DB records an editing session creates/updates/deletes.
- [ ] Generate a reviewable `.sql` file capturing those changes (idempotent /
      re-appliable INSERT/UPDATE/DELETE against the `net7` content DB, Postgres
      syntax, quoted identifiers).
- [ ] Start with the **Sector Editor**, but the mechanism must live in
      `commontools-avalonia` so **all** editors inherit it.
- [ ] Open design question to resolve in implementation: track at the DB-write
      boundary in `DB.cs` (capture every mutating command + params) vs a
      higher-level per-record dirty model. The DB-write boundary is the most
      uniform place to make it apply to all editors for free -- lean that way.

### AC.4 -- X button ALWAYS closes; never hang the UI on DB errors

- [ ] The window close button must close the window unconditionally, even mid
      DB call. No DB operation may run synchronously on the UI thread such that
      a hang or exception wedges the window.
- [ ] Root-cause and fix the `Error within DB.executeQuery()` hang (likely a
      synchronous DB call on the UI thread against the wrong/unreachable
      MySQL host that blocks until TCP timeout, plus an unhandled exception
      path that leaves the UI wedged). Move DB I/O off the UI thread; surface
      errors as a dismissable message, not a hang.
- [ ] Verify: with the DB host unreachable, the tool still opens, reports the
      connection failure, and the X button closes it immediately.

### AC.5 -- Port the Item Editor to Avalonia (`tools/itemeditor-avalonia`)

- [ ] Create `tools/itemeditor-avalonia/` mirroring the existing
      `*-avalonia` editor pattern (SDK-style csproj, net10.0, Avalonia 11.2.3,
      references `commontools-avalonia/CommonToolsAvalonia.csproj`,
      Nullable=disable to match the suite). ~14K LOC across the WinForms
      itemeditor (Database / Editors / Record Managers / Search / Widgets).
- [ ] Port it ONTO the AC.1--AC.4 base (Postgres DB layer, zero-friction login,
      change-tracking, never-hang UI) -- not onto the old MySQL base.
- [ ] Flip the `toolslauncher-avalonia` `Ported` flag for Item Editor so it is
      no longer greyed `(not yet ported)`.
- [ ] Build clean: `dotnet build` 0 warnings / 0 errors; launches via `just`.

### AC.6 -- Verify across the suite

- [ ] Sector / Mob / Mission / Item (and the rest) all: launch via `just`,
      Login with no typing (or skip), load data from `net7`, make an edit,
      produce a `.sql` changeset, and close cleanly via the X button.
- [ ] Re-apply a generated changeset against `net7` and confirm the edit lands
      and the live server reflects it (closes the loop on "the editors edit a
      dead DB").

## Dependencies / ordering

AC.1 (Postgres backend) is the foundation -- AC.2/AC.3/AC.4 all build on it, and
AC.5 (Item Editor port) must land on top of all four. Order:
AC.1 -> AC.4 (never-hang, since it interacts with the connection path) ->
AC.2 -> AC.3 -> AC.5 -> AC.6.

## Notes

- Sourcing/naming neutral per the no-disclose rule; nothing here touches Net-7
  binaries.
- No em-dashes in committed files.
