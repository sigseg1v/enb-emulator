#!/usr/bin/env pwsh
# Bring up / converge all DigitalOcean infrastructure: droplet, reserved IP,
# firewall, container registry, the Route53 A record, and the Let's Encrypt
# cert. Idempotent -- safe to re-run; re-running also RENEWS the cert when it
# is within ~30 days of expiry.
#
# SAFE BY DEFAULT: with no flag this only prints the terraform plan and applies
# NOTHING -- so you always see what would change (e.g. a droplet REPLACEMENT,
# which wipes the droplet-local DB) before it happens. Pass -y / -Apply to
# actually converge the infrastructure.
param([Alias('y')][switch]$Apply)
. "$PSScriptRoot/_Common.ps1"
Import-DeployEnv

Initialize-TerraformBackend

if (-not $Apply) {
    Invoke-Terraform plan -input=false
    Write-Host ""
    Write-Host "DRY RUN -- nothing was applied. Review the plan above."
    Write-Host "To apply it, run:  just up -y"
    return
}

Invoke-Terraform apply -input=false -auto-approve

Write-Host ""
Write-Host "Infrastructure applied."
Write-Host ("  Reserved IP : {0}" -f (Get-TfOutput 'reserved_ip'))
Write-Host ("  Registry    : {0}" -f (Get-TfOutput 'registry_endpoint'))
Write-Host ("  DNS         : {0}" -f (Get-TfOutput 'dns_status'))
Write-Host ""
Write-Host "Next: just update   (builds + pushes the images, then ships them to the droplet)"
