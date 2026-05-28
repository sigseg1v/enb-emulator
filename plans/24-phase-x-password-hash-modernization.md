# Phase X — Password hash modernization (MD5 → Argon2id)

Goal: replace the `UPPER(MD5(plaintext))` password storage and verification
across the live server tree with a modern memory-hard password hashing
scheme (preferred: **Argon2id** via libsodium; acceptable fallback: **bcrypt**
with cost ≥ 12). No backwards compatibility, no dual-read path, no MD5
fallback — every existing hash in the `accounts.password` column is
discarded and re-set at the next login (or via a forced migration sweep).

User directive (2026-05-28): "check why the fuck we hash pw with md5 and
find all occurrences of that and switch to something modernly secure with
proper params such as bcrypt" + "no back compat needed".

## Why this exists

MD5 is cryptographically broken for password storage on three independent
axes that the kyp-era / Net-7-era codebase ignored:

1. **No salt.** `MD5(plaintext)` is deterministic across all users. Identical
   passwords produce identical column values, which is a rainbow-table
   precondition. A 32-char hex stored in `accounts.password` is uniquely
   identifying — once a single user's plaintext is leaked, every other
   user with the same password is compromised.
2. **No work factor.** A single MD5 evaluation is ~1ns. A commodity GPU
   farm does ~10^11 MD5/s. Every weak password in a database dump is
   exhaustively searchable in seconds. Password hashing schemes invented
   after 2000 (PBKDF2, bcrypt, scrypt, Argon2) deliberately burn
   100ms–1s per attempt and parametrize the work factor (rounds /
   memory) so it can be ratcheted as hardware improves. MD5 cannot.
3. **Cryptanalytic attacks.** MD5 collision attacks have been practical
   since 2004 (Wang/Yu) and chosen-prefix collisions since 2007. Not the
   primary concern for password storage (preimage > collision matters
   here), but the cryptographic community considers MD5 unfit for any
   security purpose, including password hashing.

The current scheme also has secondary problems:
- `UPPER(...)` adds no security and is just an upstream-compatibility
  artifact (MySQL's MD5 returned lowercase; whoever migrated wanted
  case-stable rows). It survives the libpqxx Postgres rewrite as a
  cosmetic carry-over.
- The plaintext is sent over `/AuthLogin` (HTTPS in production after Phase
  V) but the server then computes `digest($2, 'md5')` inside the
  SQL query — the plaintext is concatenated into the query plan and ends
  up in `pg_stat_statements` text mode unless the operator deliberately
  redacts it. Modern KDFs hash on the client side of the DB boundary
  (in the C++ login process) and only bind the **resulting hash** as a
  query parameter, so plaintext never reaches Postgres at all.

## In-scope MD5 occurrences (verified 2026-05-28)

Live code paths that MUST migrate:

1. **`login-server/Net7SSL/LinuxAuth.cpp:238`** — `ValidateAccountLinux`
   (the production Linux/Postgres login validator):
   ```cpp
   "SELECT id FROM accounts WHERE username = $1 "
   "AND password = UPPER(encode(digest($2, 'md5'), 'hex'))"
   ```
   This is the call that decides every successful login on the running
   Linux server. Must be rewritten to read the stored Argon2id PHC string
   and call `crypto_pwhash_str_verify` (or equivalent) in-process.

2. **`server/src/AccountManager.cpp`**:
   - **`:174-175`** — `ValidateAccount` (legacy, MySQL-Connector/C path,
     compiled but only used if the launcher routes to the in-process
     MySQL backend instead of LinuxAuth). Symmetric to LinuxAuth path.
   - **`:195`** — `AddUser` (`sprintf(pass, "MD5('%s')", password)` —
     account creation path).
   - **`:237`** — `ChangePassword`
     (`"UPDATE accounts SET password = MD5(?) WHERE username = ?"`).

3. **`db/postgres/seed.sql:57`** — `accounts.password varchar(40) NOT NULL`.
   `varchar(40)` was sized for `UPPER(MD5(x))` (32 char hex + slack).
   Argon2id PHC strings run ~96 characters
   (`$argon2id$v=19$m=65536,t=3,p=4$<salt>$<hash>`), bcrypt PHC strings
   are 60 characters. The column needs widening to at least `varchar(128)`
   or `text`.

4. **Test fixtures** — every per-account row in
   `tests/integration/CliClient.IntegrationTests/Fixtures/seed.sql`
   currently inserts `UPPER(encode(digest('testpw', 'md5'), 'hex'))`. All
   82 rows (`cli_test01` through `cli_test82`, plus `cli_test_status0`)
   need their password hash regenerated for the new scheme. A short
   helper script that emits the PHC string for `'testpw'` is preferable
   to hand-pasting ~83 hashes — the test seed should call a small
   PL/pgSQL function or read from a pre-baked constant in a fixture
   header so adding `cli_test83` doesn't require running argon2 by hand.

Out-of-scope MD5 occurrences:

- **`login-server/Net7SSL/AccountManager.cpp:155,176,217`** — these are
  the *legacy MySQL-flavoured* AccountManager methods that have never
  compiled on Linux (use `sprintf_s` / `_snprintf_s`, `\`backticked\`` SQL
  identifiers, MySQL `MD5()` function). They live in `login-server/Net7SSL/`
  but the Linux build path goes through `LinuxAuth.cpp`. Phase X deletes
  these (they're already on the Phase Q kyp-cluster-deletion radar) or
  rewrites them to call the same new code path as `LinuxAuth`. No
  separate migration needed.

- **`proxy/third_party/openssl-3.0.16/**`** — OpenSSL ships MD5 because
  TLS 1.2 cipher suites still optionally use it for handshake MAC. Not a
  password-hashing context; do not touch.

- **`tools/*-avalonia/obj/project.assets.json`** — NuGet integrity sha512s
  that incidentally start with "3MD5". Not actually MD5 anywhere; ignore.

- **`docs/16-integration-tests.md:83`** — documentation reference to the
  test-fixture hash; update once the fixture format changes.

## Design

- **Hash function:** Argon2id (libsodium's `crypto_pwhash_str` /
  `crypto_pwhash_str_verify` with the `INTERACTIVE` profile — m=64MiB,
  t=3, p=1). Argon2id is the winner of the 2015 Password Hashing
  Competition and the default recommendation of OWASP and the IETF
  (`draft-irtf-cfrg-argon2`).
- **Why libsodium specifically:** the proxy and login-server already
  link OpenSSL 3 (Phase E) which has Argon2 in 3.2+, but our pinned
  version is 3.0.16. Pulling in libsodium (single-purpose hashing/AEAD
  library, well-audited, MIT licence — compatible with the project's
  CC BY-NC-SA-3.0 default) is cleaner than upgrading the whole OpenSSL
  vendor tree just for one new function.
- **Fallback if libsodium is unavailable in the target environment:**
  bcrypt with cost=12 via the OpenBSD reference implementation
  (`crypt_blowfish` — public domain). Bcrypt has 30+ years of attack
  history, is widely deployed (Django, Postgres `pgcrypto.crypt(...,
  gen_salt('bf', 12))`, every modern auth stack), and is dramatically
  better than MD5. Phase X picks Argon2id as preferred but the plan
  documents both so the wave-1 implementer can pick based on what
  vendors smoothly into the existing build system.
- **Column shape:** rename `accounts.password` to `accounts.password_phc`
  (TEXT, NOT NULL) to make grep'ing for any lingering MD5 path obvious.
  PHC strings are self-describing — algorithm, parameters, salt, and hash
  all live in the same string, so future scheme changes don't require a
  second column or a separate `algorithm` enum.
- **No back-compat:** existing `accounts.password` MD5 hashes are
  discarded. Per user directive "no back compat needed". The migration
  flushes every row's password and either (a) forces a password reset on
  first login via existing AddUser/ChangePassword flow, or (b) for a dev
  environment, regenerates from a known plaintext via a one-shot script.
  Production-style hash rotation (read both old and new during a
  cut-over window) is explicitly **not** done.
- **Per-attempt cost calibration:** wave-1 picks Argon2id INTERACTIVE
  (~70ms per `crypto_pwhash_str_verify` on a modern CPU). Document the
  reasoning so a future wave can ratchet to MODERATE (~700ms) if the
  login server's CPU budget allows. Bcrypt cost=12 (~250ms) if the
  fallback path is taken.

## Waves

- [ ] **W1: dependency + build wiring.**
  - Pick Argon2id-via-libsodium vs bcrypt-via-crypt_blowfish based on
    vendoring effort. Default: libsodium static into
    `login-server/third_party/libsodium/`. Add a `THIRD_PARTY_BINARIES.md`
    if the source build is non-trivial, but prefer a from-source build
    in CMake.
  - Wire into `login-server/Net7SSL/CMakeLists.txt`.
  - Add to MinGW cross-build (Phase W) if the proxy ever needs the same
    primitive — currently only the login-server validates passwords, so
    proxy stays out of scope.

- [ ] **W2: column rename + migration.**
  - `db/postgres/schema.sql`: drop `password varchar(40)`, add
    `password_phc text NOT NULL`.
  - Write a one-shot migration script that drops the column and re-adds
    it (per "no back-compat") — explicitly does NOT preserve the existing
    MD5 hashes. Document this destructive step in `db/postgres/README.md`
    and in the migration comment.

- [ ] **W3: server-side rewrite.**
  - `LinuxAuth.cpp::ValidateAccountLinux` — replace the SQL `digest()`
    comparison with `SELECT password_phc FROM accounts WHERE username =
    $1`, then call `crypto_pwhash_str_verify(stored.c_str(),
    password, plaintext_len) == 0`.
  - `server/src/AccountManager.cpp::AddUser` — compute the PHC string
    in-process (`crypto_pwhash_str`) before any SQL bind. Stored value
    is the entire PHC string verbatim. **Delete the `sprintf(pass,
    "MD5('%s')", password)` line** — the plaintext-in-SQL pattern is a
    primary motivation for this phase.
  - `server/src/AccountManager.cpp::ChangePassword` — same pattern.
  - `server/src/AccountManager.cpp::ValidateAccount` — if still reachable,
    bind the stored PHC + the plaintext and call `crypto_pwhash_str_verify`.
    If not reachable on the Linux server (LinuxAuth takes precedence),
    delete the function rather than leaving a dead MD5 path.

- [ ] **W4: test fixture regeneration.**
  - `tests/integration/CliClient.IntegrationTests/Fixtures/seed.sql` —
    replace `UPPER(encode(digest('testpw', 'md5'), 'hex'))` with a
    pre-computed Argon2id PHC string for `'testpw'`, or better, with a
    PL/pgSQL helper function the fixture calls per-row (so adding
    `cli_testNN` rows doesn't require running argon2 by hand each time).
  - Update `docs/16-integration-tests.md:83` accordingly.

- [ ] **W5: secondary cleanups.**
  - Delete or rewrite `login-server/Net7SSL/AccountManager.cpp` (the
    legacy MySQL-flavoured methods). If kept, they call the same
    `crypto_pwhash_str` path as LinuxAuth.
  - Audit `pg_stat_statements` redaction docs — even after the migration,
    `LinuxAuth.cpp` should bind the stored PHC as `$1` (not concatenate
    it), so the plan plus the verify-against-stored-PHC pattern keeps
    plaintext entirely client-side of Postgres.
  - Note in the decisions log that Phase X chose Argon2id over bcrypt
    (or vice versa) with the rationale.

## Non-goals (deliberately deferred)

- **Pepper / HSM-stored secret.** Could be added later as a wave-6 if
  the operator wants defence-in-depth against a Postgres-only compromise.
  Current scope: per-user random salt baked into the PHC string by
  libsodium. Pepper is a defence against a stolen database alone; per
  the project's preservation goals + dev-environment-first stance, adding
  a deployment secret to the build now is over-engineering.
- **Rate-limiting on `/AuthLogin`.** Orthogonal to the hash; goes in a
  proxy-level or login-server-level throttle plan. The hash modernization
  is what lets a successful password guess take 70ms instead of 1ns;
  per-IP throttling is a separate hardening axis.
- **Multi-factor.** Not in scope for a preservation project of a 2002-2004
  MMO.
- **Migrating the legacy login-server's MFC dialogs.** `Net7Mysql` is
  Windows-only legacy code that's already on Phase Q's deletion radar.
  Phase X doesn't carry it forward.

## Verification

After W1-W4 land:

```
# Build
just build

# Integration test suite (uses regenerated fixtures)
just test

# Specifically the auth/login path
cd tests/integration/CliClient.IntegrationTests
dotnet test --filter "FullyQualifiedName~GlobalConnect"
dotnet test --filter "FullyQualifiedName~MasterJoin"

# Grep guard: no MD5 should remain in any live login-server / server /
# fixture path:
git grep -nE 'MD5|md5' -- 'login-server/Net7SSL/LinuxAuth.cpp' \
    'server/src/AccountManager.cpp' \
    'tests/integration/CliClient.IntegrationTests/Fixtures/seed.sql' \
    'db/postgres/schema.sql' \
    'db/postgres/seed.sql'
# (expected: no matches)
```

## CLAUDE.md / server-integrity notes

The wire protocol is unchanged. Clients (retail Win32 client, the CLI
client, the launcher) all POST plaintext over HTTPS `/AuthLogin`; this
phase does not touch the request format or the protocol stage. The change
is entirely internal to how the server stores and verifies the value.

Per the project's server-integrity rule: this is a **server-tightening**
change (rejecting trivially-cracked password hashes), not a relaxation.
The "the real server used MD5" carry-over argument doesn't apply — the
retail Earth & Beyond server's password storage was never on the wire,
so a server-internal modernization with no protocol change cannot violate
preservation fidelity. The wire-observable behaviour (successful login
issues a ticket; failed login returns the same error code) is unchanged.
