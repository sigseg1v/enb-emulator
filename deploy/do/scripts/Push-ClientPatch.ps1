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
#   GalaxyMap.dat       manifest.json
#
# The Lua mod runtime (enbmod.dll) is published here and self-updated like the
# MVAS pair. The shared Lua bootstrap (scripts/init.lua + scripts/lib/) ships in
# the zip package (just package-client-windows -> bin/scripts/) and is NOT force-
# synced -- it is user-editable. The MODS themselves (scripts/mods/<id>/) are
# published as per-mod zips under mods/<id>-<hash>.zip and updated by the launcher
# against ./mods/<id>/modhash; a user's own mod (unknown id, no modhash) is never
# touched. See freya/client-injection/enbmod/MOD-STRUCTURE.md for the full
# contract.
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
    @{ Local = (Join-Path $dist 'bin/GalaxyMap.dat');     Rel = 'bin/GalaxyMap.dat';     Key = 'GalaxyMap.dat';         Ctype = 'application/octet-stream' },
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

# ---- package the enbmod Lua mods (see freya/client-injection/enbmod/MOD-STRUCTURE.md) ----
#      Each mod folder in the repo is hashed (deterministic, content-addressed,
#      timestamp-independent) and zipped as mods/<id>-<hash>.zip. The login
#      server hands the {id,hash} set to the launcher, which compares each
#      against its local ./mods/<id>/modhash and only re-downloads on a
#      mismatch -- so we never touch a user's own mod (unknown id, no modhash).

# Deterministic 10-char folder hash. Enumerate files (excluding any 'modhash'),
# hash each file's CONTENTS, key by forward-slash relative path, sort ordinal,
# then SHA-256 the "<relpath>\n<contenthash>\n" concatenation and take 10 hex
# chars. Order-stable + timestamp-independent + add/remove-sensitive by design.
function Get-ModFolderHash {
    param([Parameter(Mandatory)][string]$ModDir)
    $entries = foreach ($f in (Get-ChildItem -LiteralPath $ModDir -Recurse -File)) {
        if ($f.Name -eq 'modhash') { continue }
        $rel = [System.IO.Path]::GetRelativePath($ModDir, $f.FullName).Replace('\', '/')
        $ch  = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        [pscustomobject]@{ Rel = $rel; Hash = $ch }
    }
    if (-not $entries) { throw "Mod folder '$ModDir' has no files to hash." }
    $sorted = $entries | Sort-Object -Property Rel -Culture '' -CaseSensitive
    $sb = [System.Text.StringBuilder]::new()
    foreach ($e in $sorted) {
        [void]$sb.Append($e.Rel); [void]$sb.Append("`n")
        [void]$sb.Append($e.Hash); [void]$sb.Append("`n")
    }
    $bytes  = [System.Text.Encoding]::UTF8.GetBytes($sb.ToString())
    $digest = [System.Security.Cryptography.SHA256]::HashData($bytes)
    $hex    = ($digest | ForEach-Object { $_.ToString('x2') }) -join ''
    return $hex.Substring(0, 10)
}

$modsSrcDir = Join-Path $script:RepoRoot 'freya/client-injection/enbmod/scripts/mods'
$modZips = @()           # @{ Id; Hash; Local; Key }
$modManifest = @()       # @{ id; hash } for manifest.json
if (Test-Path $modsSrcDir) {
    Write-Host ""
    Write-Host "==> packaging enbmod Lua mods from $modsSrcDir"
    foreach ($modDir in (Get-ChildItem -LiteralPath $modsSrcDir -Directory | Sort-Object Name)) {
        $id = $modDir.Name
        if ($id -notmatch '^[A-Za-z0-9._-]+$') {
            throw "Mod id '$id' is not a safe single path segment ([A-Za-z0-9._-]+); rename the folder."
        }
        $hash = Get-ModFolderHash -ModDir $modDir.FullName
        $zipPath = Join-Path ([System.IO.Path]::GetTempPath()) ("mod-$id-$hash.zip")
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        # Zip the folder CONTENTS (wildcard), so extraction yields files directly
        # inside ./mods/<id>/ with no extra nesting. Compression-only; the zip's
        # own timestamps do not affect the hash (computed from source above).
        Compress-Archive -Path (Join-Path $modDir.FullName '*') -DestinationPath $zipPath -Force
        $key = "mods/$id-$hash.zip"
        Write-Host ("  mod {0,-18} {1}  -> {2}" -f $id, $hash, $key)
        $modZips     += @{ Id = $id; Hash = $hash; Local = $zipPath; Key = $key }
        $modManifest += @{ id = $id; hash = $hash }
    }
} else {
    Write-Host "==> no enbmod mods dir ($modsSrcDir); skipping mod packaging"
}

# ---- operator-supplied game-data patches (deploy/do/patches/<name>) ----------
#      A patch is a large, operator-provided Win32 EXECUTABLE the launcher runs
#      against the player's EnB game install (enb-patch.exe is ~200MB). It is
#      gitignored (every operator supplies their own); we never build it. Each
#      one is content-hashed (SHA-512), recorded in a local manifest under
#      deploy/do/patches/manifest.json, and uploaded to s3://<bucket>/patches/<name>
#      ONLY when its hash differs from what is already in the bucket -- the
#      bucket object carries the hash as user metadata (sha512), so we skip the
#      expensive re-upload of an unchanged 200MB file. The login server hands the
#      {name, sha512} set to the launcher in /updateCheck; the launcher applies a
#      patch whose hash it has not already recorded in patchlevel.txt at the EnB
#      install root. See deploy/do/README.md "Operator-provided client patch".
$patchesDir = Join-Path $script:RepoRoot 'deploy/do/patches'
$knownPatchNames = @('enb-patch.exe')   # add future patch executables here
$patchUploads = @()      # @{ Name; Local; Sha; Key } for files that must be (re)uploaded
$patchManifest = @()     # @{ name; sha512 } for manifest.json + the local record
if (Test-Path $patchesDir) {
    Write-Host ""
    Write-Host "==> reconciling operator patches in $patchesDir"
    foreach ($name in $knownPatchNames) {
        if ($name -notmatch '^[A-Za-z0-9._-]+$') {
            throw "Patch name '$name' is not a safe single path segment ([A-Za-z0-9._-]+)."
        }
        $local = Join-Path $patchesDir $name
        if (-not (Test-Path $local)) {
            Write-Host "  $name : not present; skipping (operator must supply it)"
            continue
        }
        $sha = (Get-FileHash -Algorithm SHA512 -Path $local).Hash.ToLowerInvariant()
        $key = "patches/$name"
        $patchManifest += @{ name = $name; sha512 = $sha }

        # Compare against the hash stored as user metadata on the bucket object.
        # head-object exits non-zero when the object is absent -- treat that as
        # "must upload" rather than an error (so do NOT use Invoke-Native here).
        $remoteSha = (& aws s3api head-object --bucket $bucket --key $key `
            --query 'Metadata.sha512' --output text 2>$null)
        if ($LASTEXITCODE -ne 0) { $remoteSha = '' }
        $remoteSha = ("$remoteSha".Trim())
        if ($remoteSha -eq 'None') { $remoteSha = '' }   # aws prints None for a missing key

        if ($remoteSha -eq $sha) {
            Write-Host ("  {0,-16} {1}  (already in bucket; skip upload)" -f $name, $sha)
        } else {
            Write-Host ("  {0,-16} {1}  (changed/new; will upload)" -f $name, $sha)
            $patchUploads += @{ Name = $name; Local = $local; Sha = $sha; Key = $key }
        }
    }

    # Local record manifest, so the operator can see what hash the deploy uses.
    $localPatchManifest = @{ patches = @($patchManifest) } | ConvertTo-Json -Depth 5
    Set-Content -Path (Join-Path $patchesDir 'manifest.json') -Value $localPatchManifest
} else {
    Write-Host "==> no patches dir ($patchesDir); skipping operator patches"
}

# ---- write manifest.json (files: relativePath + sha512; mods: id + hash;
#      patches: name + sha512) -- the login server synthesizes all the URLs. ----
$manifestObj = @{
    files   = @($artifacts | ForEach-Object { @{ relativePath = $_.Rel; sha512 = $_.Sha } })
    mods    = @($modManifest)
    patches = @($patchManifest)
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
    # Mod zips next (still before the manifest, so the manifest never references a
    # not-yet-uploaded zip). Each zip name carries its hash, so re-uploading an
    # unchanged mod is a content-identical overwrite under the same key.
    foreach ($m in $modZips) {
        Write-Host "==> s3 cp $($m.Key)"
        Invoke-Native aws s3 cp $m.Local "s3://$bucket/$($m.Key)" --content-type 'application/zip' --only-show-errors
    }
    # Operator patches next (still before the manifest). Only the ones whose hash
    # changed are uploaded; the hash rides as user metadata so the next deploy can
    # skip an unchanged 200MB file. --metadata-directive REPLACE is implicit for a
    # fresh PUT (cp from a local file), so the metadata is written on every upload.
    foreach ($p in $patchUploads) {
        Write-Host "==> s3 cp $($p.Key)  (~$([math]::Round((Get-Item $p.Local).Length / 1MB)) MB)"
        Invoke-Native aws s3 cp $p.Local "s3://$bucket/$($p.Key)" `
            --content-type 'application/octet-stream' --metadata "sha512=$($p.Sha)" --only-show-errors
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
    #      The mod zips invalidate by their EXACT key too (never '/mods/*'): the
    #      wildcard is the very glob-mangling hazard called out above, and since
    #      each zip's name embeds its hash a changed mod is a NEW key that was
    #      never cached -- so concrete keys are both safe and sufficient.
    #      Operator patches invalidate by their EXACT key too, and ONLY the ones
    #      actually re-uploaded this run -- an unchanged patch we skipped is byte-
    #      identical at the edge, so invalidating it would needlessly re-pull a
    #      200MB object.
    $invalidationPaths = @('/manifest.json') +
        ($artifacts    | ForEach-Object { "/$($_.Key)" }) +
        ($modZips      | ForEach-Object { "/$($_.Key)" }) +
        ($patchUploads | ForEach-Object { "/$($_.Key)" })
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
    foreach ($m in $modZips) { Remove-Item $m.Local -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
Write-Host "Published manifest + $($artifacts.Count) artifacts + $($modZips.Count) mod zip(s) + $($patchUploads.Count) patch upload(s) ($($patchManifest.Count) patch(es) in manifest) to $bucket; CloudFront invalidation has fully propagated."
Write-Host "Restart login to re-read the manifest: just apply-update   (runs automatically next if you used 'just update')."
