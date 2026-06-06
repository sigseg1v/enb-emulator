# Provider + Terraform version pins for the DigitalOcean production deploy.
# State lives in S3 (see backend.tf) -- never on the local machine.
terraform {
  required_version = ">= 1.10.0" # 1.10 = native S3 state locking (use_lockfile)

  required_providers {
    digitalocean = {
      source  = "digitalocean/digitalocean"
      version = "~> 2.43"
    }
    aws = {
      # Used ONLY for the Route53 A record + the Let's Encrypt DNS-01
      # challenge. The servers run on DigitalOcean; AWS is just DNS.
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    acme = {
      source  = "vancluever/acme"
      version = "~> 2.0"
    }
    tls = {
      source  = "hashicorp/tls"
      version = "~> 4.0"
    }
    local = {
      source  = "hashicorp/local"
      version = "~> 2.5"
    }
  }
}
