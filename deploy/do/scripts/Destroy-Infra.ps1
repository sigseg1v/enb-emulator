#!/usr/bin/env pwsh
# Tear DOWN all DigitalOcean infrastructure: droplet, reserved IP, firewall,
# container registry, the Route53 record, and the LE cert resources. This
# DESTROYS the droplet and its Postgres data volume -- player accounts/avatars
# created on the server are GONE. The S3 state bucket is NOT touched.
#
# Requires -Confirm to proceed (guard against accidental teardown).
param([switch]$Confirm)
. "$PSScriptRoot/_Common.ps1"
Import-DeployEnv

if (-not $Confirm) {
    Write-Host "This DESTROYS the droplet + its database volume (accounts/avatars lost)."
    Write-Host "Re-run with -Confirm to proceed:  ./Destroy-Infra.ps1 -Confirm"
    exit 1
}

Initialize-TerraformBackend
Invoke-Terraform destroy -input=false -auto-approve
Write-Host "Infrastructure destroyed. The S3 tfstate bucket was left intact."
