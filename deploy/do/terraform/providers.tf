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

# CloudFront, the ACM cert it uses, and a CLOUDFRONT-scoped WAFv2 ACL MUST live
# in us-east-1 regardless of where everything else runs (an AWS requirement for
# CloudFront). This alias pins those Phase AN patcher resources there even if
# var.aws_region is something else. Credentials are the same provider chain
# (AWS_PROFILE / AWS_* env) as the default aws provider.
provider "aws" {
  alias  = "us_east_1"
  region = "us-east-1"
}

provider "acme" {
  server_url = var.acme_server_url
}
