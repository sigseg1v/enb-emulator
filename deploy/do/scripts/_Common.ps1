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
        $val = $t.Substring($eq + 1).Trim().Trim('"')
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

function Get-SshArgs {
    return @('-i', (Resolve-Path $env:SSH_PRIVATE_KEY_PATH).Path, '-o', 'StrictHostKeyChecking=accept-new')
}

function Invoke-RemoteShell([string]$ReservedIp, [string]$Command) {
    Invoke-Native ssh @(Get-SshArgs) "root@$ReservedIp" $Command
}

function Copy-ToRemote([string]$ReservedIp, [string]$LocalPath, [string]$RemotePath) {
    Invoke-Native scp @(Get-SshArgs) $LocalPath "root@${ReservedIp}:${RemotePath}"
}
