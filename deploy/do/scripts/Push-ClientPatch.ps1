#!/usr/bin/env pwsh
# Phase AN -- publish a new FreyaLauncher self-update set.
#
# Builds the Windows client bundle (repo-root `just package-client-windows`),
# writes manifest.json over the three artifacts, uploads all of it to the PRIVATE
# patcher S3 bucket, and invalidates the CloudFront cache so the new build is
# served immediately. The login server only re-reads the manifest at startup, so
# run `just update` afterward to make the running login container pick it up.
#
# Bucket layout is FLAT (everything at the bucket root, fronted by CloudFront):
#
#   FreyaLauncher.exe   FreyaLauncher.cfg   FreyaProxy.exe   manifest.json
#
# The proxy's manifest relativePath is "bin/FreyaProxy.exe" (its place in the
# launcher's install tree), but it is stored FLAT as FreyaProxy.exe -- the login
# server maps the relativePath to base+"/FreyaProxy.exe" when it answers
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
    @{ Local = (Join-Path $dist 'bin/FreyaProxy.exe');    Rel = 'bin/FreyaProxy.exe';    Key = 'FreyaProxy.exe';        Ctype = 'application/octet-stream' }
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
    Write-Host ""
    Write-Host "==> cloudfront create-invalidation /*"
    Invoke-Native aws cloudfront create-invalidation --distribution-id $distId --paths '/*'
}
finally {
    Remove-Item $manifestPath -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Published manifest + 3 artifacts to $bucket and invalidated CloudFront."
Write-Host "Next: just update   (restarts login so it re-reads the new manifest)."
