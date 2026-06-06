#!/usr/bin/env pwsh
# Start the stack on the droplet (does NOT pull new images -- use Update-Stack
# for that). Brings up whatever is already deployed in /opt/enb.
. "$PSScriptRoot/_Common.ps1"
Import-DeployEnv

$ip = Get-TfOutput 'reserved_ip'
$remote = 'cd /opt/enb && docker compose --env-file .env -f docker-compose.prod.yml up -d && docker compose --env-file .env -f docker-compose.prod.yml ps'
Invoke-RemoteShell $ip $remote
Write-Host "Started on $ip."
