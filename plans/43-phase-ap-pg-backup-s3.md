# Phase AP -- rolling Postgres backups to a private S3 bucket (sidecar)

Owner request (2026-06-08, verbatim): "also add a new phase to set up hourly pg
dump for the past 24h and pg dump every 6h for 7 days past that, rolling,
compressed, backed up to s3 via a sidecar with a special s3 bucket scoped token
into a private s3 bucket."

**Scope: code only. NOTHING is deployed** -- no S3 bucket created, no IAM token
minted, no `terraform apply`, no droplet change. Every piece is opt-in and
default-OFF; an existing deploy that does not set the new `.env` fields is
completely unaffected.

## What it is

A `db-backup` sidecar container that dumps the server's two Postgres databases
(`net7` content + `net7_user` save-state) on a schedule and rolls the dumps into
a **private** S3 bucket, pruning by count so storage is bounded. It is a
read-only consumer of the databases: Postgres (read) + one S3 bucket
(write/list/delete via a bucket-scoped token). No EnB protocol, no inbound port,
no game-state mutation -- so the wire-fidelity gate (CLI byte-pin + plans/29 CV)
does NOT apply, and it does not touch the server's security posture.

### Retention model (matches the request)

| Tier        | Cadence            | Keep                  | Window          |
|-------------|--------------------|-----------------------|-----------------|
| hourly      | top of every hour  | newest 24             | last 24h        |
| six-hourly  | 00/06/12/18 UTC    | newest 28             | 7 days x 4/day  |

The six-hourly tier is a **server-side S3 copy promotion** of the hourly dump
already taken that hour -- no second `pg_dump`. Dumps use `pg_dump -Fc` (the
PostgreSQL custom format, zlib-compressed). Keys embed a UTC timestamp and sort
lexicographically, so "newest N" is the last N keys in sorted order.

A defense-in-depth S3 lifecycle rule (terraform) also expires hourly objects
after 1 day and six-hourly after 8 days, so the bucket cannot grow without bound
even if the sidecar's count-prune ever stalls.

## The bucket-scoped token

`deploy/do/terraform/backup.tf` mints a dedicated IAM user whose ONLY permission
is `ListBucket`/`GetBucketLocation` on this one bucket plus
`PutObject`/`GetObject`/`DeleteObject` on its objects -- no console, no other
bucket, no other AWS API. That is the "special s3 bucket scoped token." The
bucket blocks all public access and is SSE-S3 encrypted; unlike the patcher
bucket there is no CloudFront in front of it -- it is never served to the net.

## Files

- `db-backup/backup.sh` -- the rolling loop (bash; pg_dump + aws s3). Logs to
  stderr; default-idles when no bucket is set; `BACKUP_RUN_ONCE` / fixed
  `BACKUP_INTERVAL_SECONDS` test hooks.
- `db-backup/Dockerfile` -- `postgres:16-alpine` (so pg_dump matches the server
  DB major) + `aws-cli` + `bash`.
- `db-backup/README.md` -- config, the scoped-token rationale, deploy + restore.
- `deploy/do/terraform/backup.tf` -- private bucket + public-access-block + SSE +
  lifecycle + bucket-scoped IAM user/policy/key + outputs. All
  `count = var.manage_db_backup ? 1 : 0`.
- `deploy/do/terraform/variables.tf` -- 5 new opt-in vars (default-OFF).
- `deploy/do/scripts/_Common.ps1` -- derives `manage_db_backup` from the bucket
  name; threads `TF_VAR_db_backup_*`.
- `deploy/do/scripts/Update-Stack.ps1` -- threads bucket + scoped token into the
  droplet `.env` (secrets, never committed).
- `deploy/do/scripts/Build-And-Push.ps1` -- adds `db-backup` to the image list +
  version regex.
- `deploy/do/.env.example` -- documents the new opt-in fields.
- `docker-compose.yml` (dev) + `deploy/do/compose/docker-compose.prod.yml` --
  the `db-backup` service, default-OFF.

## Checklist

- [x] AP-1 backup.sh (dump+upload, six-hourly promote, count-prune, idle-OFF) --
      shellcheck CLEAN, `bash -n` OK, idle path smoke-tested in-container.
- [x] AP-2 Dockerfile (`postgres:16-alpine` + aws-cli + bash) -- `docker build`
      green; runs and idles with no bucket.
- [x] AP-3 terraform backup.tf + 5 variables (all opt-in, count-gated).
- [x] AP-4 PowerShell wiring (_Common TF_VAR derive; Update-Stack droplet .env;
      Build-And-Push image list).
- [x] AP-5 compose services (dev + prod), default-OFF.
- [x] AP-6 .env.example + db-backup/README.md.
- [ ] AP-7 OWNER deploy step (opt-in): set `ENB_DB_BACKUP_S3_BUCKET`, `just up`,
      copy the emitted token into `.env`, `just update`. Left to the owner --
      this phase deploys NOTHING.

## Notes

- Default-OFF proven: with `BACKUP_S3_BUCKET` empty the container logs one line
  and idles (no crash-loop). terraform creates zero resources when
  `manage_db_backup=false`.
- `terraform validate`/`apply` not run here (no AWS creds in this env, and we are
  explicitly not deploying). The HCL is `count`-gated and mirrors the proven
  patcher.tf shape.
