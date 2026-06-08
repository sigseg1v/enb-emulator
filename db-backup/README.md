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
2. Upload to `s3://<bucket>/<prefix>/hourly/<db>/<UTC-timestamp>.dump`.
3. At every 6th hour (00, 06, 12, 18 UTC) **promote** that same object into
   `<prefix>/six-hourly/<db>/` with a server-side S3 copy (no second dump).
4. Prune each tier to its retention count.

Retention (per database, per tier):

| Tier         | Key prefix              | Keep (default)            | Window      |
|--------------|-------------------------|---------------------------|-------------|
| hourly       | `<prefix>/hourly/`      | `HOURLY_RETENTION` = 24   | last 24h    |
| six-hourly   | `<prefix>/six-hourly/`  | `SIXHOURLY_RETENTION` = 28| 7 days x 4  |

So you always have the last 24 hourly snapshots plus 7 days of 6-hourly
snapshots beyond that, rolling. Timestamps sort lexicographically, so "newest N"
is just the last N keys in sorted order.

The bucket also carries an S3 **lifecycle** rule (set by terraform) that expires
hourly objects after 1 day and six-hourly after 8 days -- defense in depth, so
the bucket cannot grow without bound even if the sidecar's prune ever stalls.

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
| `BACKUP_S3_PREFIX`      | `pg`               | key prefix; must match terraform `db_backup_s3_prefix` |
| `BACKUP_S3_ENDPOINT`    | (empty = real AWS) | S3-compatible endpoint (MinIO, R2) for local tests |
| `HOURLY_RETENTION`      | `24`               | hourly objects to keep                             |
| `SIXHOURLY_RETENTION`   | `28`               | six-hourly objects to keep                         |
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

Entirely opt-in. In `deploy/do/.env`:

```
ENB_DB_BACKUP_S3_BUCKET=your-unique-enb-backups
ENB_DB_BACKUP_S3_PREFIX=pg
```

1. `just up` -- terraform creates the private bucket + the scoped IAM token.
2. Copy the emitted token into `.env`:
   ```
   terraform output -raw db_backup_access_key_id     -> ENB_DB_BACKUP_AWS_ACCESS_KEY_ID
   terraform output -raw db_backup_secret_access_key -> ENB_DB_BACKUP_AWS_SECRET_ACCESS_KEY
   ```
3. `just update` -- threads bucket + token into the droplet `.env`; the
   `db-backup` container starts dumping on the next top-of-hour.

Leaving `ENB_DB_BACKUP_S3_BUCKET` blank makes terraform create nothing and the
sidecar idle -- an existing deploy is untouched.

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
aws s3 cp s3://<bucket>/pg/hourly/net7/<ts>.dump /tmp/net7.dump
pg_restore --clean --if-exists -h <host> -U net7 -d net7 /tmp/net7.dump
```
