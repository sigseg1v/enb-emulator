#!/usr/bin/env pwsh
# Correct avatars left with negative credits on the live droplet.
#
# Background: the Linux credit-serialization bug (Player::SaveCreditLevel writing
# a 4-byte value where the save path expected 8 -- fixed in 421cd9bd) could
# persist a garbage credit balance. avatar_level_info.credits is numeric(20,0),
# which DOES accept negatives, so a corrupted row can sit at e.g. -4e9. This
# tool finds every such row and (with -Apply) clamps it to a sane floor.
#
# SAFE BY DEFAULT: with no -Apply it only SELECTs and prints the offending rows;
# nothing is mutated. Run it once to see the damage, then re-run with -Apply.
# Idempotent: after a successful -Apply there are no rows with credits < 0, so a
# second run is a no-op.
#
# No value is concatenated into SQL -- the floor is bound as a psql \set literal
# and re-quoted with :'floor'. avatar_level_info lives in net7_user.
param(
    [int]$Floor = 100000,
    [switch]$Apply
)
. "$PSScriptRoot/../scripts/_Common.ps1"
Import-DeployEnv

if ($Floor -lt 0) { throw "Floor must be >= 0 (got $Floor)." }

$ip = Get-TfOutput 'reserved_ip'
Write-Host "== Droplet root@$ip  (net7_user.avatar_level_info) =="
Write-Host ""

# ---- inspect: which avatars are negative, and by how much ----
# Join avatar_data for a human-readable name (avatar_data + avatar_level_info are
# both keyed by avatar_id, both in net7_user -- a same-DB join, no cross-DB read).
$inspectSql = @'
SELECT l.avatar_id,
       COALESCE(d.first_name, '(no name)') AS name,
       l.credits
FROM avatar_level_info l
LEFT JOIN avatar_data d ON d.avatar_id = l.avatar_id
WHERE l.credits < 0
ORDER BY l.credits ASC;
'@
$rows = @(Invoke-RemotePsql -ReservedIp $ip -Database 'net7_user' -PsqlFlags @('-tA') -Sql $inspectSql |
    Where-Object { $_ -and $_.Trim() -ne '' })

Write-Host "-- avatars with negative credits: $($rows.Count) --"
foreach ($r in $rows) {
    $f = $r.Split('|')
    Write-Host ("  avatar_id={0,-8} {1,-20} credits={2}" -f $f[0], $f[1], $f[2])
}
Write-Host ""

if ($rows.Count -eq 0) {
    Write-Host ">>> nothing to fix."
    return
}

if (-not $Apply) {
    Write-Host ">>> DRY RUN. Re-run with -Apply to clamp these to $Floor:"
    Write-Host "      just fix-negative-credits --apply"
    return
}

# ---- apply: clamp every negative row to the floor ----
$fixSql = @"
\set floor '$Floor'
UPDATE avatar_level_info SET credits = :'floor'::numeric WHERE credits < 0;
"@
Invoke-RemotePsql -ReservedIp $ip -Database 'net7_user' -Sql $fixSql | Out-Null
Write-Host ">>> clamped $($rows.Count) row(s) to $Floor credits."
Write-Host "    Affected players should relog so the server reloads the corrected balance."
