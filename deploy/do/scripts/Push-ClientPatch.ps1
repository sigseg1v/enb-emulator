#!/usr/bin/env pwsh
# Phase AN -- publish a new FreyaLauncher self-update set.
#
# Builds the Windows client bundle (repo-root `just package-client-windows`),
# writes manifest.json over the published artifacts, uploads all of it to the PRIVATE
# patcher S3 bucket, and invalidates the CloudFront cache so the new build is
# served immediately. The login server only re-reads the manifest at startup, so
# run `just update` afterward to make the running login container pick it up.
#
# Bucket layout is FLAT (everything at the bucket root, fronted by CloudFront):
#
#   FreyaLauncher.exe   FreyaLauncher.cfg   FreyaProxy.exe
#   FreyaPosFeed.dll    FreyaInject.exe     enbmod.dll
#   manifest.json
#
# The Lua mod runtime (enbmod.dll) is published here and self-updated like the
# MVAS pair. The Lua SCRIPTS (scripts/*.lua) are deliberately NOT published: they
# are user-editable mod content (init.lua hot-reloads) and force-syncing them
# would clobber a modder's local edits. They ship once in the zip package
# (just package-client-windows -> bin/scripts/), not through self-update.
#
# The proxy and the MVAS injection pair carry "bin/" manifest relativePaths (their
# place in the launcher's install tree), but are stored FLAT at the bucket root --
# the login server maps the relativePath to base+"/<filename>" when it answers
# /updateCheck (see login-server/Net7SSL/LinuxAuth.cpp HandleUpdateCheck), and the
# launcher writes it back under bin/ on download. manifest.json itself carries
# ONLY relativePath + sha512 (the login server synthesizes the URLs), matching
# what PatcherManifest.cpp parses.
#
# Opt-in: this is a no-op error unless the patcher is configured
# (ENB_PATCHER_PRIVATE_S3_BUCKET in .env -> terraform stood up the bucket +
# distribution). See deploy README "Launcher self-update".
. "$PSScriptRoot/_Common.ps1"
Import-DeployEnv

$bucket = Get-EnvOr 'ENB_PATCHER_PRIVATE_S3_BUCKET' ''
if (-not $bucket) {
    throw "Patcher not configured: set ENB_PATCHER_PRIVATE_S3_BUCKET in .env and run 'just up' first. See deploy README 'Launcher self-update'."
}

# CloudFront distribution id from terraform (the only place it exists). This also
# proves the patcher infra was actually applied, not just named in .env.
$distId = Get-TfOutput 'patcher_cloudfront_id'
if (-not $distId) {
    throw "terraform output patcher_cloudfront_id is empty -- run 'just up -y' to stand up the patcher infra before pushing a client patch."
}
# Prefer the bucket terraform actually created (authoritative over .env).
$tfBucket = Get-TfOutput 'patcher_s3_bucket'
if ($tfBucket) { $bucket = $tfBucket }

Write-Host "Patcher bucket : $bucket"
Write-Host "CloudFront     : $distId"

# ---- build the Windows client bundle (repo-root recipe) ----
Write-Host ""
Write-Host "==> building the Windows client bundle (just package-client-windows)"
Push-Location $script:RepoRoot
try {
    Invoke-Native just package-client-windows
}
finally {
    Pop-Location
}

$dist = Join-Path $script:RepoRoot 'dist/enb-client-windows'

# Each artifact: local path on disk, its manifest relativePath, and the FLAT key
# it is stored under in the bucket (the proxy flattens bin/ -> root).
$artifacts = @(
    @{ Local = (Join-Path $dist 'FreyaLauncher.exe');     Rel = 'FreyaLauncher.exe';     Key = 'FreyaLauncher.exe';     Ctype = 'application/octet-stream' },
    @{ Local = (Join-Path $dist 'FreyaLauncher.cfg');     Rel = 'FreyaLauncher.cfg';     Key = 'FreyaLauncher.cfg';     Ctype = 'text/plain' },
    @{ Local = (Join-Path $dist 'bin/FreyaProxy.exe');    Rel = 'bin/FreyaProxy.exe';    Key = 'FreyaProxy.exe';        Ctype = 'application/octet-stream' },
    @{ Local = (Join-Path $dist 'bin/FreyaPosFeed.dll');  Rel = 'bin/FreyaPosFeed.dll';  Key = 'FreyaPosFeed.dll';      Ctype = 'application/octet-stream' },
    @{ Local = (Join-Path $dist 'bin/FreyaInject.exe');   Rel = 'bin/FreyaInject.exe';   Key = 'FreyaInject.exe';       Ctype = 'application/octet-stream' },
    @{ Local = (Join-Path $dist 'bin/enbmod.dll');        Rel = 'bin/enbmod.dll';        Key = 'enbmod.dll';            Ctype = 'application/octet-stream' }
)

foreach ($a in $artifacts) {
    if (-not (Test-Path $a.Local)) {
        throw "Expected build artifact missing: $($a.Local). Did 'just package-client-windows' succeed?"
    }
}

# ---- compute lowercase-hex SHA-512 of each artifact (the form the manifest
#      publishes and BOTH sides compare; the hash compare is case-insensitive on
#      both sides anyway, but lowercase matches UpdateLogic.ComputeSha512). ----
foreach ($a in $artifacts) {
    $a.Sha = (Get-FileHash -Algorithm SHA512 -Path $a.Local).Hash.ToLowerInvariant()
    Write-Host ("  sha512 {0,-20} {1}" -f $a.Rel, $a.Sha)
}

# ---- write manifest.json (relativePath + sha512 only; login synthesizes URLs) ----
$manifestObj = @{
    files = @($artifacts | ForEach-Object { @{ relativePath = $_.Rel; sha512 = $_.Sha } })
}
$manifestJson = $manifestObj | ConvertTo-Json -Depth 5
$manifestPath = Join-Path ([System.IO.Path]::GetTempPath()) ("manifest-" + [System.Guid]::NewGuid().ToString('N') + '.json')
Set-Content -Path $manifestPath -Value $manifestJson -NoNewline

try {
    # ---- upload: binaries FIRST, manifest LAST (so a manifest in the bucket
    #      never references a not-yet-uploaded artifact). ----
    Write-Host ""
    foreach ($a in $artifacts) {
        Write-Host "==> s3 cp $($a.Key)"
        Invoke-Native aws s3 cp $a.Local "s3://$bucket/$($a.Key)" --content-type $a.Ctype --only-show-errors
    }
    Write-Host "==> s3 cp manifest.json"
    Invoke-Native aws s3 cp $manifestPath "s3://$bucket/manifest.json" --content-type 'application/json' --only-show-errors

    # ---- invalidate the edge cache so the new build is served at once ----
    #      Invalidate the EXACT keys we just uploaded, never '/*': a literal
    #      glob metacharacter can be re-expanded by a downstream shell (e.g. an
    #      aws wrapper that forwards $@ unquoted) into the local filesystem's
    #      root listing, so the wildcard silently invalidates nothing real.
    #      Concrete object paths have no metacharacters and can't be mangled,
    #      and they're exactly what changed.
    $invalidationPaths = @('/manifest.json') + ($artifacts | ForEach-Object { "/$($_.Key)" })
    Write-Host ""
    Write-Host "==> cloudfront create-invalidation $($invalidationPaths -join ' ')"
    $invId = Invoke-Native aws cloudfront create-invalidation `
        --distribution-id $distId --paths @invalidationPaths `
        --query 'Invalidation.Id' --output text --no-cli-pager
    $invId = "$invId".Trim()
    if (-not $invId) { throw "create-invalidation did not return an Invalidation.Id" }

    # ---- BLOCK until the invalidation has fully propagated to every edge ----
    #      create-invalidation returns immediately with Status=InProgress; the old
    #      manifest can still be served from the edge for ~1-5 min after that. The
    #      caller (`just update`) restarts login right after this script, and login
    #      fetches manifest.json FROM CloudFront at startup and caches the hashes
    #      with no TTL -- so if we returned early, login could re-read the STALE
    #      manifest and cache the old hashes again. Waiting here closes that race:
    #      login only restarts once every edge serves the new manifest.
    Write-Host "==> waiting for invalidation $invId to complete (CloudFront, ~1-5 min)..."
    Invoke-Native aws cloudfront wait invalidation-completed `
        --distribution-id $distId --id $invId --no-cli-pager
    Write-Host "    invalidation $invId completed"
}
finally {
    Remove-Item $manifestPath -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Published manifest + $($artifacts.Count) artifacts to $bucket; CloudFront invalidation has fully propagated."
Write-Host "Restart login to re-read the manifest: just apply-update   (runs automatically next if you used 'just update')."
