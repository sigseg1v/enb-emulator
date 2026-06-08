#!/usr/bin/env pwsh
# Snapshot the live droplet's Postgres (both databases) to backup/ before any
# operation that could touch or replace the droplet -- `just apply-update` and
# `just up`. pg_dumpall captures roles + every database in one restorable file.
#
# Why this guard exists: the droplet's pgdata is a local docker volume with NO
# off-droplet persistence (no DigitalOcean block volume). A droplet REPLACE --
# which terraform does whenever user_data changes, e.g. the registry docker
# credentials rotate -- wipes that volume and re-seeds an empty DB. So we take a
# dump immediately before the risky step.
#
# Tolerant of a not-yet-deployed stack: if terraform has no `reserved_ip` output
# (first-ever `just up`, before any droplet exists) we SKIP and exit 0 -- there
# is nothing to back up, and the deploy must not be blocked. A real dump failure
# against an EXISTING droplet, by contrast, is fatal: we will not let a risky
# apply proceed on a failed backup.
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
