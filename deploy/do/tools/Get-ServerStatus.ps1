#!/usr/bin/env pwsh
# Report live status of the deployed stack over SSH:
#   - container health + uptime (docker compose ps)
#   - players currently online
#   - which sectors those players occupy
#
# What "online" means here: the server's OWN definition. On boot it runs
#   UPDATE accounts SET last_logout = now() WHERE last_login > last_logout
# (server/src/ServerManager.cpp:243) to clear stale sessions, sets last_login on
# login (AccountManager.cpp:130), and last_logout on logout. So an account with
# last_login > last_logout is genuinely online. The authoritative in-memory
# count (GlobMemMgr::GetPlayerCount) is not reachable over SSH without a client,
# but this DB predicate is exactly what the server uses to track it.
#
# "Sectors" are started on demand and tracked only in server memory
# (SectorManager::m_SectorOnline) -- there is no DB table of running sectors, so
# we report the distinct sectors that online players currently occupy (an
# empty, idle-loaded sector won't show). Sector names live in the net7 content
# DB; we resolve them in-memory rather than via a cross-DB join (which a single
# connection cannot do).
. "$PSScriptRoot/../scripts/_Common.ps1"
Import-DeployEnv
$ip = Get-TfOutput 'reserved_ip'

Write-Host "== Droplet root@$ip =="
Write-Host ""

# ---- containers (Status column carries uptime, e.g. 'Up 3 hours (healthy)') ----
Write-Host "-- containers --"
Invoke-RemoteShell $ip "cd /opt/enb && docker compose --env-file .env -f docker-compose.prod.yml ps --format 'table {{.Service}}\t{{.Status}}'"
Write-Host ""

# ---- online players (net7_user; accounts + avatar_* are all in net7_user) ----
# Account is online when a.last_login > a.last_logout. LEFT JOIN so an account
# sitting in character-select (no entered avatar) still shows, as '(no char)'.
$onlineSql = @'
SELECT a.username,
       COALESCE(d.first_name, '(no char)') AS character,
       COALESCE(i.sector::text, '-')       AS sector
FROM accounts a
LEFT JOIN avatar_info i ON i.account_id = a.id AND i.last_login > i.last_logout
LEFT JOIN avatar_data d ON d.avatar_id = i.avatar_id
WHERE a.last_login > a.last_logout
ORDER BY a.username;
'@
# -tA => tuples-only, unaligned; unaligned mode's default field separator is '|'.
$rows = @(Invoke-RemotePsql -ReservedIp $ip -Database 'net7_user' -PsqlFlags @('-tA') -Sql $onlineSql |
    Where-Object { $_ -and $_.Trim() -ne '' })

Write-Host "-- players online: $($rows.Count) --"
if ($rows.Count -gt 0) {
    # Resolve sector id -> name from the net7 content DB (small id+name pull,
    # joined in-memory; no value is concatenated into SQL).
    $nameById = @{}
    $sectorRows = @(Invoke-RemotePsql -ReservedIp $ip -Database 'net7' -PsqlFlags @('-tA') `
        -Sql 'SELECT id, name FROM sectors;' | Where-Object { $_ -and $_.Trim() -ne '' })
    foreach ($sr in $sectorRows) {
        $i = $sr.IndexOf('|')
        if ($i -ge 0) { $nameById[$sr.Substring(0, $i)] = $sr.Substring($i + 1) }
    }

    $parsed = foreach ($r in $rows) {
        $f = $r.Split('|')
        [pscustomobject]@{
            Username  = $f[0]
            Character = $f[1]
            Sector    = if ($f[2] -ne '-' -and $nameById.ContainsKey($f[2])) { "$($f[2]) $($nameById[$f[2]])" } else { $f[2] }
        }
    }
    $parsed | Format-Table -AutoSize | Out-String | Write-Host

    $sectorsOccupied = @($parsed | Where-Object { $_.Sector -ne '-' } | Select-Object -ExpandProperty Sector -Unique)
    Write-Host "-- sectors occupied: $($sectorsOccupied.Count) --"
    foreach ($s in ($sectorsOccupied | Sort-Object)) { Write-Host "  $s" }
}
