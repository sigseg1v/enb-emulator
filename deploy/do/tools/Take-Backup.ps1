#!/usr/bin/env pwsh
# Snapshot the live droplet's Postgres (both databases) to backup/ before any
# operation that could touch or replace the droplet -- `just apply-update` and
# `just up`. pg_dumpall captures roles + every database in one restorable file.
#
# Why this guard exists: it is a safety net before any operation that could
# touch or replace the droplet, so a known-good off-box dump always exists first.
# (pgdata itself now lives on a durable block volume that survives droplet
# replacement -- see README "Durable database storage" -- so this is belt-and-
# suspenders, not the sole line of defence it once was.)
#
# Tolerant of a stack that is not up: we SKIP and exit 0 (the deploy must not be
# blocked) when there is genuinely nothing to dump --
#   * terraform has no `reserved_ip` output (first-ever `just up`, no droplet); OR
#   * the droplet exists but the stack is not deployed/running yet (no
#     /opt/enb/.env, or the postgres container is not up). This is exactly the
#     window during the durable-volume cutover: `just up -y` has replaced the
#     droplet but the first `apply-update` has not shipped the stack yet.
# A dump failure against a RUNNING postgres, by contrast, is fatal: we will not
# let a risky apply proceed on a failed backup.
#
# The droplet IP is read from terraform output (never hardcoded -- committed
# files must not contain a server IP). The dump lands in the gitignored
# deploy/do/backup/ as enb-backup-<UTC-date>.sql.

. "$PSScriptRoot/../scripts/_Common.ps1"
Import-DeployEnv

# Resolve the droplet's reserved IP. No output => infra not stood up yet => skip.
$ip = $null
try { $ip = Get-TfOutput 'reserved_ip' } catch { $ip = $null }
if ([string]::IsNullOrWhiteSpace($ip)) {
    Write-Host "take-backup: no deployed infra (no reserved_ip output) -- nothing to back up, skipping."
    exit 0
}

# Is the stack actually deployed and postgres running? On a freshly replaced
# droplet (durable-volume cutover) the stack has not been shipped yet -- no
# /opt/enb/.env, no postgres container -- so there is nothing to dump. Skip
# cleanly rather than fail the apply-update that is about to deploy it. Only a
# dump failure against a RUNNING postgres (below) is fatal.
$probe = "test -f /opt/enb/.env && cd /opt/enb && docker compose --env-file .env -f docker-compose.prod.yml ps --status running -q postgres 2>/dev/null"
$pgId  = & ssh @(Get-SshArgs) "root@$ip" $probe 2>$null
if ([string]::IsNullOrWhiteSpace($pgId)) {
    Write-Host "take-backup: stack not deployed / postgres not running on the droplet -- nothing to back up, skipping."
    exit 0
}

$backupDir = Join-Path $PSScriptRoot '../backup'
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$dest  = Join-Path $backupDir "enb-backup-$stamp.sql"
$tmp   = "$dest.partial"

Write-Host "take-backup: dumping all databases from root@$ip -> backup/enb-backup-$stamp.sql ..."

# pg_dumpall over ssh. -T: no TTY (we are piping). Output is captured to a
# .partial first so a failed/short dump never clobbers a good prior backup.
$remote = "cd /opt/enb && docker compose --env-file .env -f docker-compose.prod.yml exec -T postgres pg_dumpall -U net7"
& ssh @(Get-SshArgs) "root@$ip" $remote > $tmp
$sshExit = $LASTEXITCODE

if ($sshExit -ne 0) {
    Remove-Item -Force $tmp -ErrorAction SilentlyContinue
    throw "take-backup: pg_dumpall over ssh FAILED (exit $sshExit). Refusing to proceed -- any prior backup/enb-backup-$stamp.sql is left intact."
}

$size = (Get-Item $tmp).Length
if ($size -lt 1024) {
    Remove-Item -Force $tmp -ErrorAction SilentlyContinue
    throw "take-backup: dump was only $size bytes -- refusing to treat that as a valid backup."
}

Move-Item -Force $tmp $dest
Write-Host ("take-backup: wrote backup/enb-backup-$stamp.sql ({0:N2} MiB)." -f ($size / 1MB))
