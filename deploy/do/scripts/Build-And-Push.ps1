#!/usr/bin/env pwsh
# Build the three server images for linux/amd64 and push them to the private
# DigitalOcean Container Registry as a SINGLE repository ("enb") with
# per-service version tags, so the whole stack fits DOCR's free Starter tier
# (1 repository, 500 MiB). Build context is the repo root (the server/login
# Dockerfiles COPY common/ as well as their own tree).
#
# Tagging model -- one shared, monotonic version counter across all three
# services (they always bump together):
#
#   enb:server-vN   enb:proxy-vN   enb:login-vN     <- the build just produced
#   enb:server-latest / proxy-latest / login-latest <- re-pointed at vN, but
#                                                       only AFTER every
#                                                       versioned push succeeds
#
# Retention: the newest 3 versions PER SERVICE are kept; older version tags are
# deleted AFTER the new push succeeds, then a registry garbage-collection runs
# to reclaim the now-untagged blobs (what actually frees space under the cap).
#
# -Tag overrides the version label (e.g. -Tag v9). Default auto-increments to
# one past the highest existing vN. (An override that isn't 'vN' still pushes
# and re-points -latest, but is not auto-pruned -- keep overrides to 'vN'.)
param([string]$Tag)
. "$PSScriptRoot/_Common.ps1"
Import-DeployEnv

$repo = 'enb'
$reg  = Get-RegistryEndpoint
$services = @(
    @{ Svc = 'server'; Dockerfile = 'server/Dockerfile' },
    @{ Svc = 'login';  Dockerfile = 'login-server/Dockerfile' },
    @{ Svc = 'proxy';  Dockerfile = 'proxy/Dockerfile' }
)

# ---- determine the version label ----
$existingTags = Get-DocrTags $repo
if ($Tag) {
    $version = $Tag
} else {
    $maxN = 0
    foreach ($t in $existingTags) {
        if ($t -match '^(server|login|proxy)-v(\d+)$') {
            $n = [int]$Matches[2]
            if ($n -gt $maxN) { $maxN = $n }
        }
    }
    $version = "v$($maxN + 1)"
}
Write-Host "Registry   : $reg"
Write-Host "Repository : $repo"
Write-Host "Version    : $version"

# ---- docker login (DOCR accepts the DO API token as username AND password) ----
$env:DO_TOKEN | docker login registry.digitalocean.com --username $env:DO_TOKEN --password-stdin
if ($LASTEXITCODE -ne 0) { throw "docker login to DOCR failed." }

# ---- build + push the versioned tag for each service ----
foreach ($s in $services) {
    $tagRef = "$reg/${repo}:$($s.Svc)-$version"
    Write-Host ""
    Write-Host "==> building $($s.Svc) -> $tagRef"
    Invoke-Native docker buildx build `
        --platform linux/amd64 `
        --push `
        -f (Join-Path $script:RepoRoot $s.Dockerfile) `
        -t $tagRef `
        $script:RepoRoot
}

# ---- only after ALL versioned pushes succeed: re-point the -latest tags ----
# imagetools create aliases the already-pushed manifest -- no rebuild, no extra
# storage (same digest).
Write-Host ""
foreach ($s in $services) {
    $verRef = "$reg/${repo}:$($s.Svc)-$version"
    $latRef = "$reg/${repo}:$($s.Svc)-latest"
    Write-Host "==> tagging $latRef -> $($s.Svc)-$version"
    Invoke-Native docker buildx imagetools create -t $latRef $verRef
}

# ---- prune: keep the newest 3 versions per service, delete older ----
Write-Host ""
Write-Host "Pruning old version tags (keep newest 3 per service)..."
$afterTags  = Get-DocrTags $repo
$deletedAny = $false
foreach ($s in $services) {
    $svcVers = @()
    foreach ($t in $afterTags) {
        if ($t -match "^$($s.Svc)-v(\d+)$") {
            $svcVers += [pscustomobject]@{ Tag = $t; N = [int]$Matches[1] }
        }
    }
    $ordered = $svcVers | Sort-Object N -Descending
    $keep = $ordered | Select-Object -First 3
    $drop = $ordered | Select-Object -Skip 3
    foreach ($d in $drop) {
        Write-Host "  deleting $($d.Tag)"
        Remove-DocrTag $repo $d.Tag
        $deletedAny = $true
    }
    $kept = (($keep | ForEach-Object { $_.Tag }) -join ', ')
    Write-Host "  $($s.Svc): kept [$kept]"
}

# ---- reclaim space from the now-untagged manifests ----
if ($deletedAny) { Start-DocrGarbageCollection }

Write-Host ""
Write-Host "Pushed enb:{server,login,proxy}-$version (+ -latest)."
Write-Host "Next: ./Update-Stack.ps1 -Tag $version   (or -Tag latest)"
