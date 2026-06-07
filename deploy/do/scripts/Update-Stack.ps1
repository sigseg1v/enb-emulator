#!/usr/bin/env pwsh
# Ship the compose stack + config + certs + DB seed data to the droplet and
# (re)start it. This is the "deploy a new build" command: it pulls the latest
# images from the registry and recreates only the containers whose image
# changed. Idempotent.
#
# -Tag selects which version SUFFIX to run for both services -- 'latest'
# (default, from IMAGE_TAG in .env) or a pinned 'vN'. The compose file resolves
# it as enb:server-<tag> / enb:login-<tag>. (The proxy is client-side and never
# deployed here.)
param([string]$Tag)
. "$PSScriptRoot/_Common.ps1"
Import-DeployEnv

if (-not $Tag) { $Tag = (Get-EnvOr 'IMAGE_TAG' 'latest') }

$ip       = Get-TfOutput 'reserved_ip'
$reg      = Get-RegistryEndpoint
$domain   = $env:DOMAIN_NAME
$project  = (Get-EnvOr 'PROJECT_NAME' 'enb-emulator')

Write-Host "Target droplet : root@$ip"
Write-Host "Registry/tag   : $reg @ $Tag"

# ---- stage the bundle locally ----
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("enb-deploy-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'certs')       -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'db')          -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'server-data') -Force | Out-Null

try {
    Copy-Item (Join-Path $script:ComposeDir 'docker-compose.prod.yml') (Join-Path $stage 'docker-compose.prod.yml')

    # Net7Config.cfg with __DOMAIN__ / __INTERNAL_IP__ filled in.
    $cfg = Get-Content (Join-Path $script:ComposeDir 'Net7Config.cfg') -Raw
    $cfg = $cfg.Replace('__DOMAIN__', $domain).Replace('__INTERNAL_IP__', $ip)
    Set-Content -Path (Join-Path $stage 'Net7Config.cfg') -Value $cfg -NoNewline

    # Certs: terraform issues the BOOTSTRAP cert (certs-prod/), but after the
    # first deploy the droplet renews its own cert in place via the systemd
    # timer (cloud-init). Re-shipping the local copy would clobber a freshly
    # droplet-renewed cert with a stale one -- so only ship when the droplet
    # has no cert yet (first deploy).
    $sshArgs   = Get-SshArgs
    $certState = (& ssh @sshArgs "root@$ip" "test -f /opt/enb/certs/$domain.cer && echo EXISTS || echo MISSING")
    $certState = ("$certState").Trim()
    if ($certState -eq 'EXISTS') {
        Write-Host "Certs          : droplet already has $domain.cer -- leaving the droplet-renewed cert in place."
    } else {
        $cer = Join-Path $script:CertsDir "$domain.cer"
        $key = Join-Path $script:CertsDir "$domain.pem"
        if (-not (Test-Path $cer) -or -not (Test-Path $key)) {
            throw "Missing $domain.cer/.pem in $script:CertsDir. Run Deploy-Infra (MANAGE_CERT=true) or drop the cert there manually."
        }
        Copy-Item $cer (Join-Path $stage "certs/$domain.cer")
        Copy-Item $key (Join-Path $stage "certs/$domain.pem")
        Write-Host "Certs          : shipping bootstrap $domain.cer (first deploy)."
    }

    # DB seed/schema (schema-init mounts this) + server data dir.
    Copy-Item (Join-Path $script:RepoRoot 'db/postgres/*') (Join-Path $stage 'db') -Recurse
    Copy-Item (Join-Path $script:RepoRoot 'server/data/*') (Join-Path $stage 'server-data') -Recurse

    # compose env-file consumed on the droplet.
    $envText = @(
        "COMPOSE_PROJECT_NAME=$project"
        "REGISTRY=$reg"
        "IMAGE_TAG=$Tag"
        "DOMAIN=$domain"
    ) -join "`n"
    Set-Content -Path (Join-Path $stage '.env') -Value $envText

    # ---- pack + ship ----
    $tgz = Join-Path ([System.IO.Path]::GetTempPath()) ("enb-deploy-" + [System.Guid]::NewGuid().ToString('N') + '.tgz')
    Invoke-Native tar -czf $tgz -C $stage '.'
    Copy-ToRemote $ip $tgz '/opt/enb/bundle.tgz'

    # ---- extract + pull + up on the droplet ----
    $remote = @(
        'set -e'
        'cd /opt/enb'
        'tar -xzf bundle.tgz'
        'rm -f bundle.tgz'
        'docker compose --env-file .env -f docker-compose.prod.yml pull'
        'docker compose --env-file .env -f docker-compose.prod.yml up -d'
        'docker compose --env-file .env -f docker-compose.prod.yml ps'
    ) -join '; '
    Invoke-RemoteShell $ip $remote

    Write-Host ""
    Write-Host "Stack updated on $ip (tag $Tag)."
}
finally {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
    if ($tgz -and (Test-Path $tgz)) { Remove-Item $tgz -Force -ErrorAction SilentlyContinue }
}
