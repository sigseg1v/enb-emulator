#!/usr/bin/env pwsh
# Build the three server images for linux/amd64 and push them to the private
# DigitalOcean Container Registry. Build context is the repo root (the
# server/login Dockerfiles COPY common/ as well as their own tree).
#
# -Tag overrides the image tag (default: short git SHA, plus :latest).
param([string]$Tag)
. "$PSScriptRoot/_Common.ps1"
Import-DeployEnv

if (-not $Tag) {
    $Tag = (Get-EnvOr 'IMAGE_TAG' '')
    if (-not $Tag) {
        $sha = (& git -C $script:RepoRoot rev-parse --short HEAD 2>$null)
        $Tag = if ($LASTEXITCODE -eq 0 -and $sha) { $sha.Trim() } else { 'latest' }
    }
}

$reg = Get-RegistryEndpoint
Write-Host "Registry : $reg"
Write-Host "Tag      : $Tag"

# DOCR accepts the DO API token as both docker username and password.
$env:DO_TOKEN | docker login registry.digitalocean.com --username $env:DO_TOKEN --password-stdin
if ($LASTEXITCODE -ne 0) { throw "docker login to DOCR failed." }

$images = @(
    @{ Name = 'enb-server'; Dockerfile = 'server/Dockerfile' },
    @{ Name = 'enb-login';  Dockerfile = 'login-server/Dockerfile' },
    @{ Name = 'enb-proxy';  Dockerfile = 'proxy/Dockerfile' }
)

foreach ($img in $images) {
    $name = $img.Name
    Write-Host ""
    Write-Host "==> building $name ($($img.Dockerfile))"
    Invoke-Native docker buildx build `
        --platform linux/amd64 `
        --push `
        -f (Join-Path $script:RepoRoot $img.Dockerfile) `
        -t "$reg/${name}:$Tag" `
        -t "$reg/${name}:latest" `
        $script:RepoRoot
}

Write-Host ""
Write-Host "Pushed enb-server / enb-login / enb-proxy at tag '$Tag' (+ latest)."
Write-Host "Next: ./Update-Stack.ps1 -Tag $Tag"
