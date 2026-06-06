#!/usr/bin/env pwsh
# Bring up / converge all DigitalOcean infrastructure: droplet, reserved IP,
# firewall, container registry, the Route53 A record, and the Let's Encrypt
# cert. Idempotent -- safe to re-run; re-running also RENEWS the cert when it
# is within ~30 days of expiry.
. "$PSScriptRoot/_Common.ps1"
Import-DeployEnv

Initialize-TerraformBackend
Invoke-Terraform apply -input=false -auto-approve

Write-Host ""
Write-Host "Infrastructure applied."
Write-Host ("  Reserved IP : {0}" -f (Get-TfOutput 'reserved_ip'))
Write-Host ("  Registry    : {0}" -f (Get-TfOutput 'registry_endpoint'))
Write-Host ("  DNS         : {0}" -f (Get-TfOutput 'dns_status'))
Write-Host ""
Write-Host "Next: ./Build-And-Push.ps1   then   ./Update-Stack.ps1"
