# Shared helpers for the DigitalOcean deploy scripts. Dot-sourced by each
# Verb-Noun script: `. "$PSScriptRoot/_Common.ps1"`.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# deploy/do/scripts -> deploy/do -> deploy -> repo root.
$script:ScriptDir  = Split-Path -Parent $PSCommandPath
$script:DeployRoot = Split-Path -Parent $script:ScriptDir            # deploy/do
$script:RepoRoot   = (Resolve-Path (Join-Path $script:DeployRoot '../..')).Path
$script:TfDir      = Join-Path $script:DeployRoot 'terraform'
$script:ComposeDir = Join-Path $script:DeployRoot 'compose'
$script:CertsDir   = Join-Path $script:DeployRoot 'certs-prod'
$script:EnvFile    = Join-Path $script:DeployRoot '.env'

function Import-DeployEnv {
    if (-not (Test-Path $script:EnvFile)) {
        throw ".env not found at $script:EnvFile. Copy .env.example to .env and fill it in."
    }
    foreach ($line in Get-Content $script:EnvFile) {
        $t = $line.Trim()
        if ($t -eq '' -or $t.StartsWith('#')) { continue }
        $eq = $t.IndexOf('=')
        if ($eq -lt 1) { continue }
        $key = $t.Substring(0, $eq).Trim()
        $val = $t.Substring($eq + 1).Trim()
        if ($val.StartsWith('"') -or $val.StartsWith("'")) {
            # Quoted value: the quotes delimit it; take up to the closing quote
            # so a '#' inside the quotes is preserved verbatim.
            $q   = $val[0]
            $end = $val.IndexOf($q, 1)
            $val = if ($end -gt 0) { $val.Substring(1, $end - 1) } else { $val.Substring(1) }
        } else {
            # Unquoted value: an inline comment starts at the first whitespace +
            # '#'. A '#' with no leading whitespace (e.g. inside a URL) is kept.
            $val = ([regex]::Replace($val, '\s+#.*$', '')).Trim()
        }
        Set-Item -Path "Env:$key" -Value $val
    }

    # Required, with friendly errors.
    foreach ($req in @('DO_TOKEN','DOMAIN_NAME','REGISTRY_NAME','SSH_PRIVATE_KEY_PATH','SSH_PUBLIC_KEY_PATH',
                       'TFSTATE_BUCKET','TFSTATE_REGION','TFSTATE_KEY')) {
        if (-not (Test-Path "Env:$req") -or [string]::IsNullOrWhiteSpace((Get-Item "Env:$req").Value)) {
            throw "Required .env key '$req' is missing or empty."
        }
    }

    # Map .env -> the TF_VAR_* names terraform expects, so no values live in
    # committed tfvars. AWS_* are read directly by the aws + acme providers.
    $pub = (Get-Content (Resolve-Path $env:SSH_PUBLIC_KEY_PATH) -Raw).Trim()
    $env:TF_VAR_do_token         = $env:DO_TOKEN
    $env:TF_VAR_domain_name      = $env:DOMAIN_NAME
    $env:TF_VAR_registry_name    = $env:REGISTRY_NAME
    $env:TF_VAR_ssh_public_key   = $pub
    $env:TF_VAR_aws_region       = (Get-EnvOr 'AWS_REGION' 'us-east-1')
    $env:TF_VAR_route53_zone_id  = (Get-EnvOr 'ROUTE53_ZONE_ID' '')
    $env:TF_VAR_project_name     = (Get-EnvOr 'PROJECT_NAME' 'enb-emulator')
    $env:TF_VAR_droplet_region   = (Get-EnvOr 'DROPLET_REGION' 'nyc3')
    $env:TF_VAR_droplet_size     = (Get-EnvOr 'DROPLET_SIZE' 's-2vcpu-4gb')
    $env:TF_VAR_droplet_image    = (Get-EnvOr 'DROPLET_IMAGE' 'docker-20-04')
    $env:TF_VAR_registry_tier    = (Get-EnvOr 'REGISTRY_TIER' 'basic')
    $env:TF_VAR_ssh_allowed_cidr = (Get-EnvOr 'SSH_ALLOWED_CIDR' '0.0.0.0/0')
    $env:TF_VAR_manage_dns       = (Get-EnvOr 'MANAGE_DNS' 'true')
    $env:TF_VAR_manage_cert      = (Get-EnvOr 'MANAGE_CERT' 'true')
    $env:TF_VAR_acme_email       = (Get-EnvOr 'ACME_EMAIL' '')
    $env:TF_VAR_acme_server_url  = (Get-EnvOr 'ACME_SERVER_URL' 'https://acme-v02.api.letsencrypt.org/directory')

    # Phase AN launcher-update delivery (opt-in). manage_patcher is derived: it
    # turns on only when the operator names a bucket, so an existing deploy with
    # no patcher fields stays untouched.
    $patcherBucket = (Get-EnvOr 'ENB_PATCHER_PRIVATE_S3_BUCKET' '')
    $env:TF_VAR_manage_patcher    = if ($patcherBucket) { 'true' } else { 'false' }
    $env:TF_VAR_patcher_s3_bucket = $patcherBucket
    $env:TF_VAR_patcher_dl_domain = (Get-EnvOr 'PATCHER_DL_DOMAIN' '')
    $env:TF_VAR_patcher_rate_limit = (Get-EnvOr 'PATCHER_RATE_LIMIT' '20')

    # aws provider/acme expect AWS_DEFAULT_REGION too.
    if (-not (Test-Path 'Env:AWS_DEFAULT_REGION')) { $env:AWS_DEFAULT_REGION = $env:TF_VAR_aws_region }
}

function Get-EnvOr([string]$Name, [string]$Default) {
    if ((Test-Path "Env:$Name") -and -not [string]::IsNullOrWhiteSpace((Get-Item "Env:$Name").Value)) {
        return (Get-Item "Env:$Name").Value
    }
    return $Default
}

function Invoke-Native {
    param([Parameter(Mandatory)][string]$Exe, [Parameter(ValueFromRemainingArguments)][string[]]$Args)
    & $Exe @Args
    if ($LASTEXITCODE -ne 0) { throw "$Exe exited with code $LASTEXITCODE" }
}

function Invoke-Terraform {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Args)
    Invoke-Native terraform "-chdir=$script:TfDir" @Args
}

function Initialize-TerraformBackend {
    Invoke-Terraform init -input=false `
        "-backend-config=bucket=$env:TFSTATE_BUCKET" `
        "-backend-config=key=$env:TFSTATE_KEY" `
        "-backend-config=region=$env:TFSTATE_REGION" `
        "-backend-config=encrypt=true" `
        "-backend-config=use_lockfile=true"
}

function Get-TfOutput([string]$Name) {
    $v = & terraform "-chdir=$script:TfDir" output -raw $Name 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Could not read terraform output '$Name'. Has Deploy-Infra run?" }
    return $v
}

function Get-RegistryEndpoint {
    # registry.digitalocean.com/<name>; computable without tf state.
    return "registry.digitalocean.com/$($env:REGISTRY_NAME)"
}

# ---- DigitalOcean Container Registry REST API (no doctl dependency) ----
# We publish a SINGLE repository ("enb") with per-service version tags so the
# whole stack fits DOCR's free Starter tier (1 repository). These helpers list,
# delete, and garbage-collect tags via the DO API, authenticating with the DO
# token (the same token used for `docker login`).

function Get-DocrApiBase {
    return "https://api.digitalocean.com/v2/registry/$($env:REGISTRY_NAME)"
}

function Get-DocrHeaders {
    return @{ Authorization = "Bearer $($env:DO_TOKEN)"; 'Content-Type' = 'application/json' }
}

function Get-DocrTags([string]$Repository) {
    # Tag strings in $Repository, or @() if the repo does not exist yet (404 on
    # the very first push). Follows pagination defensively.
    $tags = @()
    $url  = "$(Get-DocrApiBase)/repositories/$Repository/tags?per_page=200"
    while ($url) {
        $resp = Invoke-RestMethod -Method Get -Uri $url -Headers (Get-DocrHeaders) `
            -SkipHttpErrorCheck -StatusCodeVariable sc
        if ($sc -eq 404) { return @() }
        if ($sc -ge 400) { throw "DOCR list-tags returned HTTP $sc for repository '$Repository'." }
        if ($resp.PSObject.Properties['tags'] -and $resp.tags) {
            $tags += ($resp.tags | ForEach-Object { $_.tag })
        }
        # `links` is present only when there is a next page; under StrictMode a
        # bare `$resp.links` would throw on the single-page response. Walk the
        # chain defensively, treating any missing hop as "no more pages".
        $url = $null
        $links = $resp.PSObject.Properties['links']
        if ($links) {
            $pages = $links.Value.PSObject.Properties['pages']
            if ($pages) {
                $next = $pages.Value.PSObject.Properties['next']
                if ($next) { $url = $next.Value }
            }
        }
    }
    return $tags
}

function Remove-DocrTag([string]$Repository, [string]$Tag) {
    $url = "$(Get-DocrApiBase)/repositories/$Repository/tags/$Tag"
    $null = Invoke-RestMethod -Method Delete -Uri $url -Headers (Get-DocrHeaders) `
        -SkipHttpErrorCheck -StatusCodeVariable sc
    if ($sc -ge 400 -and $sc -ne 404) { throw "DOCR delete-tag '$Tag' returned HTTP $sc." }
}

function Start-DocrGarbageCollection {
    # Reclaims storage from now-untagged manifests. DO allows one active GC at a
    # time and briefly makes the registry read-only, so tolerate "already
    # running" / "nothing to collect" instead of failing the whole push.
    $url = "$(Get-DocrApiBase)/garbage-collection"
    $null = Invoke-RestMethod -Method Post -Uri $url -Headers (Get-DocrHeaders) `
        -SkipHttpErrorCheck -StatusCodeVariable sc
    if ($sc -lt 400) { Write-Host "  garbage-collection: started (HTTP $sc)." }
    else { Write-Host "  garbage-collection: skipped (HTTP $sc -- already running or nothing to collect)." }
}

function Get-SshArgs {
    # The droplet is cattle: terraform REPLACES it (new host key) whenever the
    # image/size/etc. changes, but it keeps the SAME reserved IP. Pinning the
    # host key in the operator's ~/.ssh/known_hosts therefore breaks every
    # rebuild with REMOTE HOST IDENTIFICATION HAS CHANGED, and "fixing" it by
    # blindly running ssh-keygen -R is just manual trust-on-first-use anyway. So
    # we keep host keys OUT of the global file (UserKnownHostsFile=/dev/null) and
    # accept-new each connect. Trade-off: this drops MITM protection on the admin
    # SSH channel; acceptable here because the box is ours and rebuilt often. If
    # you want real verification, pin a terraform-generated host key instead (see
    # README "SSH host keys").
    return @(
        '-i', (Resolve-Path $env:SSH_PRIVATE_KEY_PATH).Path,
        '-o', 'UserKnownHostsFile=/dev/null',
        '-o', 'StrictHostKeyChecking=accept-new',
        '-o', 'LogLevel=ERROR'
    )
}

# NOTE: ssh/scp are invoked DIRECTLY here, not via Invoke-Native. Invoke-Native's
# `[string[]]$Args` collects @(Get-SshArgs) as one nested array and string-joins
# it ("-i key -o StrictHostKeyChecking=..." as a single argv token), which made
# scp read the whole thing as the identity-file path. An external `&` call
# expands the array into separate argv elements correctly.
function Invoke-RemoteShell([string]$ReservedIp, [string]$Command) {
    & ssh @(Get-SshArgs) "root@$ReservedIp" $Command
    if ($LASTEXITCODE -ne 0) { throw "ssh to $ReservedIp exited with code $LASTEXITCODE" }
}

function Copy-ToRemote([string]$ReservedIp, [string]$LocalPath, [string]$RemotePath) {
    & scp @(Get-SshArgs) $LocalPath "root@${ReservedIp}:${RemotePath}"
    if ($LASTEXITCODE -ne 0) { throw "scp to $ReservedIp exited with code $LASTEXITCODE" }
}

function Invoke-RemotePsql {
    # Run SQL inside the droplet's postgres container against $Database and return
    # psql's stdout. The SQL travels over ssh STDIN -- never as a shell argument
    # -- so a value embedded with psql `\set var '...'` and re-quoted with
    # `:'var'` is parsed only by psql, never by the remote shell (a '$' in an
    # Argon2id PHC is therefore never shell-expanded). $Database is a fixed
    # literal we control ('net7' / 'net7_user'), not user input.
    param(
        [Parameter(Mandatory)][string]$ReservedIp,
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$Sql,
        [string[]]$PsqlFlags = @()
    )
    $flags  = ($PsqlFlags -join ' ')
    $remote = "cd /opt/enb && docker compose --env-file .env -f docker-compose.prod.yml " +
              "exec -T -e PGPASSWORD=net7 postgres psql -U net7 -d $Database -v ON_ERROR_STOP=1 $flags"
    $out = $Sql | & ssh @(Get-SshArgs) "root@$ReservedIp" $remote
    if ($LASTEXITCODE -ne 0) { throw "remote psql ($Database) exited with code $LASTEXITCODE" }
    return $out
}
