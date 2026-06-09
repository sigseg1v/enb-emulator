#!/usr/bin/env pwsh
# Restore a pg_dumpall snapshot (from `just take-backup`) into the live droplet's
# Postgres, replacing the current net7 + net7_user databases. This is the
# DESTRUCTIVE inverse of take-backup: it drops both databases and recreates them
# from the dump. Use it to seed the durable volume during the pgdata migration,
# or for disaster recovery.
#
#   just restore-backup backup/enb-backup-<UTC-ts>.sql
#
# Sequence (all on the droplet, postgres stays up; app containers are stopped so
# their connections release and they don't write mid-restore):
#   1. scp the dump to /opt/enb/restore.sql
#   2. stop server/login/status-notifier/db-backup
#   3. terminate live backends on net7/net7_user, DROP both databases
#   4. psql < restore.sql  (pg_dumpall recreates roles + both DBs + all data;
#      "role already exists" is expected and ignored -- we do NOT ON_ERROR_STOP
#      the restore for exactly that reason)
#   5. docker compose up -d  (brings the app back; schema-init sees tables
#      present and skips seeding)
#
# The droplet IP is read from terraform output (never hardcoded).

param(
    [Parameter(Mandatory)][string]$File,
    [switch]$Force   # skip the interactive confirmation
)

. "$PSScriptRoot/../scripts/_Common.ps1"
Import-DeployEnv

$local = Resolve-Path -LiteralPath $File -ErrorAction Stop
$size  = (Get-Item $local).Length
if ($size -lt 1024) { throw "Restore-Backup: '$File' is only $size bytes -- that is not a valid pg_dumpall." }

$ip = Get-TfOutput 'reserved_ip'

Write-Host ""
Write-Host "  RESTORE (DESTRUCTIVE)" -ForegroundColor Yellow
Write-Host ("  dump : {0} ({1:N2} MiB)" -f $local, ($size / 1MB))
Write-Host "  into : root@$ip  (DROPS + recreates net7 and net7_user)"
Write-Host ""
if (-not $Force) {
    $ans = Read-Host "Type 'restore' to proceed"
    if ($ans -ne 'restore') { Write-Host "Aborted."; exit 1 }
}

Write-Host "restore: copying dump to droplet ..."
Copy-ToRemote $ip $local.Path '/opt/enb/restore.sql'

# Build the remote sequence. compose service names match docker-compose.prod.yml.
$dc = 'docker compose --env-file .env -f docker-compose.prod.yml'
$remote = @(
    'set -euo pipefail'
    'cd /opt/enb'
    "$dc stop server login status-notifier db-backup || true"
    # Release any lingering backends, then drop. Each DROP runs autocommit (psql
    # -c), never inside a transaction block.
    "$dc exec -T postgres psql -U net7 -d postgres -v ON_ERROR_STOP=1 -c ""SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname IN ('net7','net7_user') AND pid <> pg_backend_pid();"""
    "$dc exec -T postgres psql -U net7 -d postgres -v ON_ERROR_STOP=1 -c 'DROP DATABASE IF EXISTS net7;'"
    "$dc exec -T postgres psql -U net7 -d postgres -v ON_ERROR_STOP=1 -c 'DROP DATABASE IF EXISTS net7_user;'"
    # Restore. NO ON_ERROR_STOP: pg_dumpall re-issues CREATE ROLE net7 which
    # already exists; that single error is expected and harmless.
    "$dc exec -T postgres psql -U net7 -d postgres < /opt/enb/restore.sql"
    "$dc up -d"
    'rm -f /opt/enb/restore.sql'
    "$dc ps"
) -join ' && '

Write-Host "restore: dropping + reloading databases on the droplet ..."
# Pass $remote straight through: Invoke-RemoteShell hands it to ssh as a single
# argv token and the droplet's login shell (bash) parses it. Do NOT wrap in
# `bash -c '...'` -- the SQL's own single quotes ('net7','net7_user') cannot nest
# inside that and get stripped, corrupting the query.
Invoke-RemoteShell $ip $remote
Write-Host "restore: done. Both databases reloaded from $File."
