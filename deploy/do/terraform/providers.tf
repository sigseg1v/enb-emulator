provider "digitalocean" {
  token = var.do_token
}

# AWS is used only for Route53 (DNS A record + the LE DNS-01 challenge).
# Credentials come from the standard AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY
# environment variables (exported by _Common.ps1 from .env). They only need
# route53 permissions on the one hosted zone.
provider "aws" {
  region = var.aws_region
}

provider "acme" {
  server_url = var.acme_server_url
}
