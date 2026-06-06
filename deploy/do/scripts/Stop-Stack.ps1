#!/usr/bin/env pwsh
# Stop the stack on the droplet WITHOUT destroying anything. Containers stop;
# the droplet, the pgdata volume, and all infra stay. Use Start-Stack to
# resume, or Destroy-Infra to tear the whole thing down.
. "$PSScriptRoot/_Common.ps1"
Import-DeployEnv

$ip = Get-TfOutput 'reserved_ip'
$remote = 'cd /opt/enb && docker compose --env-file .env -f docker-compose.prod.yml stop && docker compose --env-file .env -f docker-compose.prod.yml ps'
Invoke-RemoteShell $ip $remote
Write-Host "Stopped on $ip (data + infra preserved)."
