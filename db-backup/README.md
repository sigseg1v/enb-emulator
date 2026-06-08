# db-backup -- rolling Postgres -> private S3 backups (Phase AP)

A small sidecar that dumps the server's two Postgres databases on a schedule and
rolls the dumps into a **private** S3 bucket, pruning by count so storage stays
bounded. It is a **read-only** consumer of the databases: it speaks only to
Postgres (read) and to one S3 bucket (write/list/delete via a bucket-scoped
token). No EnB protocol, no inbound port, no game-state mutation.

## What it does

Every hour, for each database (`net7` content + `net7_user` save-state):

1. `pg_dump -Fc` -- the PostgreSQL **custom** format, which is zlib-compressed
   and restorable with `pg_restore`.
2. Upload to `s3://<bucket>/hourly/<db>/<UTC-timestamp>.dump`.
3. Once a day (00:00 UTC) **promote** that same object into `daily/<db>/` with a
   server-side S3 copy (no second dump).
4. Prune each tier to its retention count.

Retention (per database, per tier):

| Tier   | Key prefix     | Keep (default)         | Window        |
|--------|----------------|------------------------|---------------|
| hourly | `hourly/<db>/` | `HOURLY_RETENTION` = 24| last 24h      |
| daily  | `daily/<db>/`  | `DAILY_RETENTION` = 14 | last 14 days  |

So you always have the last 24 hourly snapshots plus the last 14 daily
snapshots, rolling. Timestamps sort lexicographically, so "newest N" is just the
last N keys in sorted order.

### Retention is count-based, NOT time-based (and why)

There is deliberately **no S3 lifecycle / expiry policy** on the bucket. The
count cap (24 hourly / 14 daily) is enforced entirely by this sidecar's prune,
which deletes an old dump **only when a fresh one has replaced it**.

This is a safety decision, not an oversight. S3 lifecycle rules can only expire
objects by **age**, never by count -- and an age-based rule is dangerous for
backups: if the sidecar ever stops producing dumps (crash, bad creds, DB down),
a time rule would keep deleting the survivors until **zero** backups remain,
during exactly the outage when you need them most. Count-based pruning instead
**freezes** the set when uploads stop: no new upload -> no delete. The trade-off
is that a wedged sidecar can leave slightly more than the nominal count around;
that is the correct direction to fail for a backup system.

## Default-OFF

With `BACKUP_S3_BUCKET` empty the script logs one line and idles forever (it
does **not** crash-loop). The service is therefore harmless to leave declared in
a stack that has not opted in. It only does work once a bucket + AWS credentials
are supplied.

## Configuration (environment)

| Var                     | Default            | Meaning                                            |
|-------------------------|--------------------|----------------------------------------------------|
| `DB_HOST`               | `postgres:5432`    | host or host:port of Postgres                      |
| `DB_USER`               | `net7`             | Postgres user                                      |
| `DB_PASS`               | `net7`             | Postgres password (-> `PGPASSWORD`)                |
| `BACKUP_DATABASES`      | `net7 net7_user`   | space-separated db list                            |
| `BACKUP_S3_BUCKET`      | (empty = idle)     | target bucket                                      |
| `BACKUP_S3_ENDPOINT`    | (empty = real AWS) | S3-compatible endpoint (MinIO, R2) for local tests |
| `HOURLY_RETENTION`      | `24`               | hourly objects to keep                             |
| `DAILY_RETENTION`       | `14`               | daily objects to keep                              |
| `AWS_ACCESS_KEY_ID`     | --                 | bucket-scoped token id (SECRET)                    |
| `AWS_SECRET_ACCESS_KEY` | --                 | bucket-scoped token secret (SECRET)                |
| `AWS_DEFAULT_REGION`    | --                 | bucket region                                      |
| `BACKUP_INTERVAL_SECONDS` | (empty)          | test override: fixed sleep instead of top-of-hour  |
| `BACKUP_RUN_ONCE`       | `0`                | run a single cycle then exit (external cron driver)|

Credentials are NEVER baked into the image; they come from the environment.

## The bucket-scoped token

`deploy/do/terraform/backup.tf` provisions an IAM user whose **only** permission
is `ListBucket`/`GetBucketLocation` on this one bucket plus
`PutObject`/`GetObject`/`DeleteObject` on its objects. No console access, no
other bucket, no other AWS API. That access key is the "special s3 bucket scoped
token": if it leaks, the blast radius is this single backup bucket.

The bucket itself blocks all public access and is encrypted at rest (SSE-S3).
Unlike the patcher bucket there is **no** CloudFront in front of it -- it is
never served to the internet.

## Deploy (DigitalOcean stack)

Entirely opt-in, and the scoped token never passes through your hands. In
`deploy/do/.env` set **only** the bucket name:

```
ENB_DB_BACKUP_S3_BUCKET=your-unique-enb-backups
```

1. `just up` -- terraform (using your AWS profile) creates the private bucket +
   the bucket-scoped IAM user, and stores the access key in the tfstate.
2. `just update` -- reads the scoped token **straight from the terraform
   outputs** (`db_backup_access_key_id` / `db_backup_secret_access_key`) and
   threads bucket + token into the droplet `.env`. The `db-backup` container
   starts dumping on the next top-of-hour.

There is no copy-paste of secrets and no access-key/secret field in `.env`: the
token lives only in the tfstate terraform already manages. Leaving
`ENB_DB_BACKUP_S3_BUCKET` blank makes terraform create nothing and the sidecar
idle -- an existing deploy is untouched.

## Local test against MinIO

Point the sidecar at a local S3-compatible endpoint:

```
BACKUP_S3_BUCKET=test BACKUP_S3_ENDPOINT=http://minio:9000 \
AWS_ACCESS_KEY_ID=minioadmin AWS_SECRET_ACCESS_KEY=minioadmin \
AWS_DEFAULT_REGION=us-east-1 BACKUP_RUN_ONCE=1 \
docker compose run --rm db-backup
```

## Restore

```
aws s3 cp s3://<bucket>/hourly/net7/<ts>.dump /tmp/net7.dump
pg_restore --clean --if-exists -h <host> -U net7 -d net7 /tmp/net7.dump
```
