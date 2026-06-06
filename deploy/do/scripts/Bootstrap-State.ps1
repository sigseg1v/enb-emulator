#!/usr/bin/env pwsh
# One-time: create the S3 bucket that holds Terraform state (versioned,
# encrypted, private). Run this ONCE before the first Deploy-Infra. Requires
# the AWS CLI + AWS creds in .env (the same creds Route53/ACME use).
#
# Chicken-and-egg: the state backend cannot create its own bucket, so this
# bootstrap uses the AWS CLI directly (no Terraform state of its own).
. "$PSScriptRoot/_Common.ps1"
Import-DeployEnv

$bucket = $env:TFSTATE_BUCKET
$region = $env:TFSTATE_REGION

Write-Host "Bootstrapping Terraform state bucket: s3://$bucket ($region)"

# create-bucket errors if it already exists/owned -- treat that as success.
try {
    if ($region -eq 'us-east-1') {
        aws s3api create-bucket --bucket $bucket --region $region | Out-Null
    } else {
        aws s3api create-bucket --bucket $bucket --region $region `
            --create-bucket-configuration "LocationConstraint=$region" | Out-Null
    }
    Write-Host "  bucket created."
} catch {
    Write-Host "  bucket already exists (or owned by you) -- continuing."
}

aws s3api put-bucket-versioning --bucket $bucket `
    --versioning-configuration Status=Enabled | Out-Null
aws s3api put-bucket-encryption --bucket $bucket `
    --server-side-encryption-configuration '{"Rules":[{"ApplyServerSideEncryptionByDefault":{"SSEAlgorithm":"AES256"}}]}' | Out-Null
aws s3api put-public-access-block --bucket $bucket `
    --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true | Out-Null

Write-Host "State bucket ready (versioned + AES256 + public access blocked)."
Write-Host "Next: ./Deploy-Infra.ps1"
