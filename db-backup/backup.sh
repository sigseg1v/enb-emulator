#!/usr/bin/env bash
#
# Phase AP -- rolling Postgres backups to a private S3 bucket.
#
# Dumps every configured database once an hour with `pg_dump -Fc` (the custom
# format is zlib-compressed), uploads each dump to a PRIVATE S3 bucket under an
# "hourly/" prefix, and at every 6th hour PROMOTES that same dump (server-side
# S3 copy, no re-dump) into a "six-hourly/" prefix. Retention is by object
# count, per database, per tier:
#
#   hourly/<db>/      keep newest HOURLY_RETENTION    (default 24  = 24h)
#   six-hourly/<db>/  keep newest SIXHOURLY_RETENTION (default 28  = 7d x 4/day)
#
# So you always have the last 24 hourly snapshots plus 7 days of 6-hourly
# snapshots beyond that, rolling. Keys embed a UTC timestamp and sort
# lexicographically, so "newest N" == the last N keys in sorted order.
#
# Talks ONLY to Postgres (read) and S3 (write/list/delete on ONE bucket via a
# bucket-scoped token). No EnB protocol, no inbound port. It is a read-only
# consumer of the databases.
#
# DEFAULT-OFF: with BACKUP_S3_BUCKET empty the loop logs once and idles forever
# (it never crash-loops), so the service is harmless to leave declared in a
# stack that has not opted in.
#
# Credentials: the standard AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY /
# AWS_REGION env vars, supplied by the operator from a bucket-scoped IAM token
# (see deploy/do/terraform/backup.tf). NEVER baked into the image.

set -uo pipefail

# ---- configuration (all overridable via env) ------------------------------
DB_HOST_PORT="${DB_HOST:-postgres:5432}"
DB_USER="${DB_USER:-net7}"
export PGPASSWORD="${DB_PASS:-net7}"
DATABASES="${BACKUP_DATABASES:-net7 net7_user}"

S3_BUCKET="${BACKUP_S3_BUCKET:-}"
S3_PREFIX="${BACKUP_S3_PREFIX:-pg}"
# Optional: S3-compatible endpoint (MinIO, R2, ...). Empty => real AWS S3.
S3_ENDPOINT="${BACKUP_S3_ENDPOINT:-}"

HOURLY_RETENTION="${HOURLY_RETENTION:-24}"
SIXHOURLY_RETENTION="${SIXHOURLY_RETENTION:-28}"

# Cadence. Default: wake at the top of every hour. Override with an explicit
# interval (seconds) for testing. RUN_ONCE=1 runs a single cycle then exits
# (useful for an external cron driver).
INTERVAL_SECONDS="${BACKUP_INTERVAL_SECONDS:-}"
RUN_ONCE="${BACKUP_RUN_ONCE:-0}"

DB_HOST="${DB_HOST_PORT%%:*}"
DB_PORT="${DB_HOST_PORT##*:}"
[ "$DB_PORT" = "$DB_HOST" ] && DB_PORT=5432

# aws CLI endpoint flag (array so an empty endpoint adds no arg).
AWS_ENDPOINT_ARGS=()
[ -n "$S3_ENDPOINT" ] && AWS_ENDPOINT_ARGS=(--endpoint-url "$S3_ENDPOINT")

# Logs go to stderr so dump_and_upload's stdout carries ONLY the uploaded key
# (captured by the caller via command substitution).
log() { echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) db-backup: $*" >&2; }

# ---- one dump+upload for a single database --------------------------------
# Returns 0 and echoes the uploaded key on success; non-zero on failure.
dump_and_upload() {
  local db="$1" ts="$2"
  local file="/tmp/${db}-${ts}.dump"
  local key="${S3_PREFIX}/hourly/${db}/${ts}.dump"

  if ! pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$db" \
        -Fc -f "$file" 2>/tmp/pg_dump.err; then
    log "ERROR pg_dump failed for '$db': $(tr '\n' ' ' < /tmp/pg_dump.err)"
    rm -f "$file"
    return 1
  fi

  if ! aws "${AWS_ENDPOINT_ARGS[@]}" s3 cp "$file" "s3://${S3_BUCKET}/${key}" \
        --only-show-errors; then
    log "ERROR s3 upload failed for '$db' -> $key"
    rm -f "$file"
    return 1
  fi

  rm -f "$file"
  log "uploaded s3://${S3_BUCKET}/${key} (hourly)"
  echo "$key"
  return 0
}

# ---- promote an hourly key into the six-hourly tier (server-side copy) -----
promote_to_six_hourly() {
  local db="$1" ts="$2" src_key="$3"
  local dst_key="${S3_PREFIX}/six-hourly/${db}/${ts}.dump"
  if aws "${AWS_ENDPOINT_ARGS[@]}" s3 cp \
        "s3://${S3_BUCKET}/${src_key}" "s3://${S3_BUCKET}/${dst_key}" \
        --only-show-errors; then
    log "promoted -> s3://${S3_BUCKET}/${dst_key}"
  else
    log "ERROR six-hourly promote failed for '$db' -> $dst_key"
  fi
}

# ---- keep only the newest $keep keys under a prefix -----------------------
prune_prefix() {
  local prefix="$1" keep="$2"
  local keys
  # sort_by ascending Key => oldest first; keys are timestamp-sortable.
  keys=$(aws "${AWS_ENDPOINT_ARGS[@]}" s3api list-objects-v2 \
            --bucket "$S3_BUCKET" --prefix "$prefix" \
            --query 'sort_by(Contents,&Key)[].Key' --output text 2>/dev/null)
  [ -z "$keys" ] || [ "$keys" = "None" ] && return 0

  # word-split (keys never contain whitespace).
  # shellcheck disable=SC2086
  set -- $keys
  local del=$(( $# - keep ))
  [ "$del" -le 0 ] && return 0

  local i=0
  for k in "$@"; do
    i=$((i + 1))
    [ "$i" -gt "$del" ] && break
    if aws "${AWS_ENDPOINT_ARGS[@]}" s3 rm "s3://${S3_BUCKET}/${k}" \
          --only-show-errors; then
      log "pruned s3://${S3_BUCKET}/${k}"
    fi
  done
}

# ---- one full backup cycle ------------------------------------------------
run_cycle() {
  local ts hour
  ts="$(date -u +%Y%m%dT%H%M%SZ)"
  hour="$(date -u +%H)"
  # strip a leading zero so 06/08 are not parsed as octal
  hour=$((10#$hour))
  local is_six_hourly=0
  [ $((hour % 6)) -eq 0 ] && is_six_hourly=1

  for db in $DATABASES; do
    local src_key
    if src_key="$(dump_and_upload "$db" "$ts")"; then
      [ "$is_six_hourly" -eq 1 ] && promote_to_six_hourly "$db" "$ts" "$src_key"
    fi
    prune_prefix "${S3_PREFIX}/hourly/${db}/"     "$HOURLY_RETENTION"
    prune_prefix "${S3_PREFIX}/six-hourly/${db}/" "$SIXHOURLY_RETENTION"
  done
}

# ---- seconds until the next top-of-hour -----------------------------------
seconds_to_next_hour() {
  local now_min now_sec
  now_min=$((10#$(date -u +%M)))
  now_sec=$((10#$(date -u +%S)))
  echo $(( (60 - now_min) * 60 - now_sec ))
}

# ---- main -----------------------------------------------------------------
if [ -z "$S3_BUCKET" ]; then
  log "BACKUP_S3_BUCKET is empty -- backups disabled, idling. Set the bucket and"
  log "AWS_* credentials to enable (see db-backup/README.md)."
  # Idle without burning CPU and without crash-looping the container.
  while true; do sleep 3600; done
fi

log "starting: dbs='${DATABASES}' bucket='${S3_BUCKET}' prefix='${S3_PREFIX}'" \
    "hourly_keep=${HOURLY_RETENTION} six_hourly_keep=${SIXHOURLY_RETENTION}" \
    "endpoint='${S3_ENDPOINT:-aws}'"

if [ "$RUN_ONCE" = "1" ]; then
  run_cycle
  exit 0
fi

while true; do
  if [ -n "$INTERVAL_SECONDS" ]; then
    sleep "$INTERVAL_SECONDS"
  else
    sleep "$(seconds_to_next_hour)"
  fi
  run_cycle
done
