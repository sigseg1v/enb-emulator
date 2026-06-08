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
$project  = (Get-EnvOr 'PROJECT_NAME' 'freya')

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
    #   __DOMAIN__       <- DOMAIN_NAME (the server resolves it via the
    #                       extra_hosts 127.0.0.1 mapping in the compose file)
    #   __INTERNAL_IP__  <- the reserved IP (advertised to clients on handoff;
    #                       see README "Remote addressing" -- UNVERIFIED vs the
    #                       real Win32 client).
    # WARNING: Net7Config.cfg must contain NO comment lines. The server's
    # config parser (server/src/Net7.cpp ProcessConfig, strtok on '='/'\n') has
    # no comment support: a '#' line before/between keys gets glued onto the
    # adjacent key and that key silently fails to parse. A comment above
    # 'domain=' is exactly what left g_DomainName empty and crash-looped the
    # server. Keep this file pure key=value.
    $cfg = Get-Content (Join-Path $script:ComposeDir 'Net7Config.cfg') -Raw
    $cfg = $cfg.Replace('__DOMAIN__', $domain).Replace('__INTERNAL_IP__', $ip)
    Set-Content -Path (Join-Path $stage 'Net7Config.cfg') -Value $cfg -NoNewline

    # Certs: terraform issues the BOOTSTRAP cert (certs-prod/), but after the
    # first deploy the droplet renews its own cert in place via the systemd
    # timer (cloud-init). Re-shipping the local copy would clobber a freshly
    # droplet-renewed cert with a stale one -- so only ship when the droplet
    # has no cert yet (first deploy).
    $certState = (& ssh @(Get-SshArgs) "root@$ip" "test -f /opt/enb/certs/$domain.cer && echo EXISTS || echo MISSING")
    if ($LASTEXITCODE -ne 0) { throw "cert probe ssh to $ip exited with code $LASTEXITCODE" }
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

    # compose env-file consumed on the droplet. This file is REWRITTEN on every
    # deploy, so anything the prod compose needs must be threaded through here --
    # editing /opt/enb/.env by hand on the droplet would be clobbered next update.
    # The Phase AM status-notifier feature is default-off: leave these blank in
    # the operator .env and the sidecar idles. STATUS_WEBHOOK_URL and
    # DISCORD_BOT_TOKEN are SECRETS; they live only in the gitignored operator
    # .env, like DO_TOKEN.
    $extStatus  = Get-EnvOr 'NET7_EXTERNAL_STATUS_ENABLED' ''
    $webhook    = Get-EnvOr 'STATUS_WEBHOOK_URL' ''
    $retention  = Get-EnvOr 'STATUS_RETENTION_DAYS' '7'
    $botToken   = Get-EnvOr 'DISCORD_BOT_TOKEN' ''
    $guildId    = Get-EnvOr 'DISCORD_GUILD_ID' ''

    # Phase AN: the login server's /updateCheck reads the launcher manifest from
    # CloudFront. Derive the two NET7_PATCHER_* env vars from the patcher fields;
    # leave them blank when the patcher is not configured (login then reports the
    # server DOWN to the launcher, fail-closed -- harmless until the infra is up).
    $patcherBucket = Get-EnvOr 'ENB_PATCHER_PRIVATE_S3_BUCKET' ''
    $patcherManifestUrl = ''
    $patcherDlBase      = ''
    if ($patcherBucket) {
        $dlDomain = Get-EnvOr 'PATCHER_DL_DOMAIN' "dl.$domain"
        $patcherDlBase      = "https://$dlDomain"
        $patcherManifestUrl = "https://$dlDomain/manifest.json"
    }

    # Phase AP rolling DB backups (default-off). Only the bucket NAME is an
    # operator choice (.env opt-in toggle). The BUCKET-SCOPED IAM token is NOT a
    # secret the operator handles: backup.tf mints it with the operator's AWS
    # profile and terraform stores it in the tfstate we already have, so we read
    # the key/secret STRAIGHT FROM THE TF OUTPUTS here -- no secret ever round-
    # trips through the operator .env. Blank bucket => no tf outputs to read and
    # the sidecar idles.
    $backupBucket    = Get-EnvOr 'ENB_DB_BACKUP_S3_BUCKET' ''
    $backupAwsKey    = ''
    $backupAwsSecret = ''
    $backupAwsRegion = Get-EnvOr 'AWS_REGION' 'us-east-1'
    if ($backupBucket) {
        $backupAwsKey    = Get-TfOutput 'db_backup_access_key_id'
        $backupAwsSecret = Get-TfOutput 'db_backup_secret_access_key'
    }

    $envText = @(
        "COMPOSE_PROJECT_NAME=$project"
        "REGISTRY=$reg"
        "IMAGE_TAG=$Tag"
        "DOMAIN=$domain"
        "NET7_EXTERNAL_STATUS_ENABLED=$extStatus"
        "STATUS_WEBHOOK_URL=$webhook"
        "STATUS_RETENTION_DAYS=$retention"
        "DISCORD_BOT_TOKEN=$botToken"
        "DISCORD_GUILD_ID=$guildId"
        "NET7_PATCHER_MANIFEST_URL=$patcherManifestUrl"
        "NET7_PATCHER_DL_BASE=$patcherDlBase"
        "BACKUP_S3_BUCKET=$backupBucket"
        "BACKUP_AWS_ACCESS_KEY_ID=$backupAwsKey"
        "BACKUP_AWS_SECRET_ACCESS_KEY=$backupAwsSecret"
        "BACKUP_AWS_REGION=$backupAwsRegion"
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
        # The bundle is tarred on the workstation, so tar -xzf (as root) restores
        # the workstation uid onto any bootstrapped cert/key. The server container
        # runs unprivileged as net7 (uid/gid 999, pinned in server/Dockerfile) and
        # fail-closes at boot if it cannot READ the DTLS cert/key. Hand ownership
        # to 999:999 so it can. No-op once the droplet-renewed cert is in place
        # (renew.sh installs it 999:999 itself). login runs as root, unaffected.
        'if ls certs/*.cer certs/*.pem >/dev/null 2>&1; then chown 999:999 certs/*.cer certs/*.pem; fi'
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
