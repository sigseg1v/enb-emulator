# Remote state in S3 (versioned + encrypted + locked). The concrete
# bucket/key/region are supplied at `terraform init` time via
# -backend-config=... by scripts/Deploy-Infra.ps1 (read from .env), so no
# account-specific values are committed here.
#
# State is in S3 and NOT on the local machine because it contains the
# Let's Encrypt private key (the acme_certificate resource). Treat the
# bucket as sensitive: it is created encrypted + private by
# scripts/Bootstrap-State.ps1.
terraform {
  backend "s3" {}
}
