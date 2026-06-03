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

> **Scope finding (2026-06-03, pre-implementation survey).** The migration is
> larger than a client-library swap:
> - The shared `commontools-avalonia` layer is the easy part (Npgsql swap +
>   parameter convention). But `ColumnData.GetName()` is dual-purpose -- it
>   builds SQL AND indexes `DataRow`s (`dataRow[columnName]`) -- so quoting can
>   NOT be centralised there; identifier quoting must happen at each SQL-build
>   site.
> - The editors mostly roll their OWN raw SQL: ~163 `SELECT/INSERT/UPDATE/DELETE`
>   string literals across the 10 editor projects, not routed through the common
>   helpers. Each must be audited.
> - The Postgres `net7` schema stores identifiers QUOTED + case-preserved. Most
>   game tables/columns are lowercase (work unquoted), but **39 identifiers are
>   mixed-case** (`EName`, `Version`, `npc_Id`, `classSpecific`, `mission_XML`,
>   `BuyMultiplyer`, ...) and MUST be double-quoted or Postgres folds them to
>   lowercase -> column-not-found (the case-folding trap from the two-DB memory).
> - Hard MySQL-isms found in the raw SQL that Postgres outright rejects:
>   `INSERT INTO t SET col=val` (no Postgres equivalent -> rewrite to
>   `INSERT (cols) VALUES (...)`), `LAST_INSERT_ID()` (-> `RETURNING` / `lastval()`),
>   `information_schema ... Auto_increment` (MySQL-only column),
>   `?param` placeholders (Npgsql needs `@param`).
> So AC.6 (per-editor raw-SQL audit) is the real bulk of this phase, not AC.1.
> AC.1 lays the foundation; each editor is then migrated + verified individually.

### AC.1 -- Migrate `commontools-avalonia` DB layer MySQL -> Postgres (Npgsql)

- [x] Replace `MySql.Data.MySqlClient` usage in
      `tools/commontools-avalonia/Database/DB.cs` with `Npgsql`
      (`NpgsqlConnection`/`NpgsqlDataAdapter`/`NpgsqlCommand`/`NpgsqlTransaction`).
      Done: `m_mySql*` -> `m_connection`/`m_transaction`; `QueryParameterCharacter`
      `?` -> `@`; `DATABASE_NAME` `Net7` -> lowercase `net7` (libpq does NOT
      case-fold the DB name in a connection string). csproj: `MySql.Data` ->
      `Npgsql 8.0.3`. Build clean (0/0).
- [x] Rewrite `LoginData.ConnStr(...)` to emit a Npgsql connection string
      (Host/Port/Database/Username/Password/Timeout/Command Timeout) for the
      `net7` content DB. Done in `Gui/Login.axaml.cs`.
- [x] Audit every SQL string the common layer issues for MySQL-isms. Done:
      fixed the `makeDatabaseVariables` codegen (information_schema queried by
      `table_schema='public'`, not by DB name; single-quoted column aliases
      `'table_name'` -> double-quoted `"table_name"`). Grepped with the Grep
      tool. (The ~163 per-editor literals remain AC.6.)
- [x] Decide the per-editor target DB explicitly: **`net7`** (content). Host-side
      it is `localhost:5434` (-> container 5432), creds net7/net7. Documented in
      `Login.axaml.cs` defaults and `ConnStr`.

**Folded in here (shared files):** AC.2's local-stack defaults + env-var prefill
(`LoginData.LoadFromEnvironment()` reading `ENB_DB_HOST/PORT/USER/PASS`, default
port 5434) and AC.4's never-broken-state connection handling (short `Timeout=5`
so an unreachable host fails fast; `openConnection()` disposes + nulls a failed
connection instead of caching a half-open one) landed in the same edit, since
they live in `DB.cs`/`Login.axaml.cs`. The remaining AC.2 (window resize, `just`
recipe env export) and AC.4 (off-UI-thread DB I/O + unconditional X-close) work
is still open.

### AC.2 -- Zero-friction login when launched via `just`

- [ ] When a tool is launched through `just` against the local stack,
      auto-populate the connection from the known local stack
      (host=localhost, host-port=5434 -> container 5432, db=net7, the dev
      credentials the compose stack uses) so the user can click Login with no
      typing -- or skip the
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

- [x] Root-caused the `Error within DB.executeQuery()` hang: editors were
      pointed at the unreachable legacy MySQL host (`net-7.org:3307`), and the
      Login window's connect + version queries ran SYNCHRONOUSLY on the UI
      thread -- the socket blocked until the (default ~30s) TCP timeout, freezing
      the window and its X button. Fixed at three levels:
      - AC.1 repoints editors at the reachable local `net7` Postgres + adds
        `Timeout=5` (the connect can no longer block for 30s).
      - `Login.AcceptedLoginInformation()` is now async: UI is read on the UI
        thread, then `conn.Open()` + `getVersion`/`setVersion` run on a worker
        thread via `Task.Run`. The UI thread (and the X button) stay responsive
        throughout; re-entrancy is guarded with `m_loginInProgress`.
      - `DBErrorReporter.Show` is marshaled to the UI thread via
        `Dispatcher.UIThread.Post` (DB errors now originate on worker threads;
        the message box must be created on the UI thread).
- [~] Unconditional X-close, the harder half: in Avalonia the title-bar X is
      dispatched on the UI thread, so it can only ever be processed when the UI
      thread is NOT blocked. The Login path is now non-blocking. The editor MAIN
      windows still run some data-load/save queries synchronously on the UI
      thread; with the warm, reachable local connection those return fast, but a
      down DB still blocks each call up to `Timeout=5`/`Command Timeout=30`.
      Moving each editor's data-load off the UI thread is per-editor work tracked
      under AC.6 -- there is NO central trick that makes a blocked UI thread
      closable, so the only real fix is keeping I/O off it everywhere.
- [ ] Verify (real-client/manual): with the DB host unreachable, each tool still
      opens, reports the failure, and the X closes it immediately. Login path
      verified by code review + build; per-editor verification is AC.6.

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
