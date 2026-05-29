# db/postgres — converted schema

## What this is

A sed-pipeline-translated Postgres version of the upstream Net-7 MySQL
schema (`db/mysql/net7.sql`) and seed data (`db/mysql/net7_user.sql`).

Run `bash db/postgres/convert.sh` to regenerate `schema.sql` and
`seed.sql` from the MySQL source.

## Phase X: password storage

The `accounts.password` column was renamed to `accounts.password_phc text`
in Phase X. The column now stores Argon2id PHC strings (libsodium's
`crypto_pwhash_str` output, ~96 chars) instead of `UPPER(MD5(plaintext))`.
This change is **destructive** -- existing MD5 hashes are NOT preserved;
they are dropped at migration time, and every account either rotates via
the normal `ChangePassword` path or is re-provisioned. The dev admin
row in `seed.sql` carries the Argon2id PHC for the plaintext `'devadmin'`.

## How to apply

Against a local Postgres 16:

```sh
psql -U net7 -d net7 -f db/postgres/schema.sql
psql -U net7 -d net7 -f db/postgres/seed.sql    # optional reference data
```

Or via the dev stack:

```sh
just dev            # brings up postgres + schema-init + server + login
just apply-schema   # re-apply (idempotent-ish; CASCADE-drops first)
```

## What `convert.sh` handles

- Backtick identifiers `` `foo` `` -> double-quoted `"foo"`.
- `ENGINE=...`, `DEFAULT CHARSET=...`, `COLLATE ...` stripped from
  `CREATE TABLE`.
- `int(N) unsigned` -> `bigint`, `int(N)` -> `integer`.
- `tinyint(N)` (signed or unsigned) -> `smallint`. See the heuristic
  note below.
- `smallint(N)` -> `smallint` (or `integer` if unsigned).
- `bigint(N)` -> `bigint` (or `numeric(20,0)` if unsigned, to preserve
  range — `bigint unsigned` can hold values beyond `int8`).
- `mediumint(N)` -> `integer`.
- `double(M,N)` / `double unsigned` / bare `double` -> `double precision`.
- `float(M,N)` -> `real`.
- `varchar(0)` -> `text` (Postgres rejects varchar(0); MySQL's
  `varchar(0)` only ever held empty strings or NULL, which `text` covers).
- `datetime` -> `timestamp`.
- `'0000-00-00 00:00:00'` and `'0000-00-00'` literals (illegal in
  Postgres) -> epoch (`'1970-01-01 ...'`). Affects both CREATE TABLE
  DEFAULT clauses and INSERT values.
- MySQL stored procedures and functions (`DELIMITER` / `CREATE DEFINER`
  blocks) are stripped wholesale — Phase N inlined every `CALL ...`
  into the C++ libpqxx path, so the procs are dead weight that won't
  parse in Postgres anyway.
- `AUTO_INCREMENT` -> `GENERATED ALWAYS AS IDENTITY` (and any
  `AUTO_INCREMENT=NNN` table option dropped).
- `DROP TABLE IF EXISTS x` -> `DROP TABLE IF EXISTS "x" CASCADE`.
- `KEY name (cols)` and `UNIQUE KEY name (cols)` inside `CREATE TABLE`
  -> rewritten as `-- INDEX TODO: ...` comments. Phase C extracts those
  into real `CREATE INDEX` statements.
- `SET FOREIGN_KEY_CHECKS=0/1;` dropped.
- `USING BTREE` clauses dropped.
- Trailing commas before closing `)` cleaned up.

## Validation status

Validated against Postgres 16 on 2026-05-26:

- **71 of 71 tables in `schema.sql` created cleanly.** No DDL errors.
- **`seed.sql` applies fully clean** under `ON_ERROR_STOP=1` — zero
  errors, zero warnings. The docker-compose `schema-init` service uses
  the strict mode so regressions break the boot rather than being
  silently swallowed.

## What `convert.sh` does NOT handle (Phase C continuation)

These don't currently produce errors against the *current* MySQL dumps,
but are semantic-translation gaps that may bite when re-running
`convert.sh` against an updated dump or when wiring up new code paths:

1. **`text` semantics**. MySQL's `text` is implicitly UTF-8 (or whatever
   `CHARSET=` says); Postgres `text` is whatever the DB encoding is. The
   names match so the column will declare; the data semantics differ.
2. **`\\` and `\n` escapes in MySQL string literals**. Postgres parses
   `'a\\b'` as the literal four-char string by default (unless you write
   `E'a\\b'`). MySQL parses it as `a\b`. The `INSERT` statements in
   `seed.sql` will need `E'...'` prefixes or a `SET standard_conforming_strings = off;`
   at the top — neither is wonderful.
3. **Binary `\\0` NUL bytes in INSERTs**. MySQL allows them in string
   literals. Postgres rejects them in `text` columns. Affected blobs
   probably want `bytea` columns instead.
4. **`0x...` hex literals**. MySQL accepts these as integer or binary
   literals. Postgres wants `x'...'` for bit strings or explicit
   `decode(..., 'hex')` for `bytea`.
5. **`tinyint(1)` as boolean**. The convert mapping is `smallint`, not
   `boolean`, deliberately — heuristically detecting "this tinyint is
   really a bool" is risky on a 71-table schema. Audit per-column in
   Phase C.
6. **`KEY` extraction**. The TODO comments need to be turned into real
   `CREATE INDEX "name" ON "table" (cols);` statements.
7. **Foreign keys**. The original schema declares
   `SET FOREIGN_KEY_CHECKS=0;` to skip FK validation on load. If any
   `CONSTRAINT` clauses survive, Postgres will enforce them on insert.
8. **Reserved-word column names** (`order`, `user`, etc.) — these were
   safe inside backticks in MySQL; now double-quoted in Postgres they
   still need quoting on every reference. Application code must match.
9. **MySQL functions in DEFAULTs** like `CURRENT_TIMESTAMP` are mostly
   fine in Postgres, but `ON UPDATE CURRENT_TIMESTAMP` is not — has to
   become a trigger.

## Phase C continuation items

- Walk every `-- INDEX TODO:` and convert to real `CREATE INDEX`.
- Audit `tinyint(1)` columns and migrate the genuine booleans to
  `boolean`.
- Replace any binary-data columns with `bytea` and re-encode the seed
  inserts via `decode(..., 'hex')`.
- Wire up the server's DB layer to libpqxx (Phase C scope).
- Move `seed.sql` data ingestion out of `psql` and into a small loader
  that handles the binary edge cases properly.

## File map

| File | Purpose |
|---|---|
| `convert.sh` | sed-pipeline MySQL -> Postgres translator (idempotent) |
| `schema.sql` | converted DDL (regenerated from `db/mysql/net7.sql`) |
| `seed.sql`   | converted reference data (from `db/mysql/net7_user.sql`) |
| `README.md`  | this file |
