variable "do_token" {
  description = "DigitalOcean API token (read/write)."
  type        = string
  sensitive   = true
}

variable "aws_region" {
  description = "AWS region for the provider (Route53 is global; any region works)."
  type        = string
  default     = "us-east-1"
}

variable "route53_zone_id" {
  description = "Route53 hosted zone ID that owns the domain (e.g. Z0123...)."
  type        = string
  default     = ""
}

variable "domain_name" {
  description = "Fully-qualified hostname players connect to, e.g. enb.sigsegv.land."
  type        = string
}

variable "project_name" {
  description = "DigitalOcean project name to group the resources under."
  type        = string
  default     = "enb-emulator"
}

variable "droplet_region" {
  description = "DigitalOcean region slug for the droplet + reserved IP."
  type        = string
  default     = "nyc3"
}

variable "droplet_size" {
  # Stack idles ~1.5GB (server ~975MiB + postgres ~140MiB + OS/dockerd). 2GB is
  # the dev/single-tester floor (~500MiB headroom); bump to s-2vcpu-4gb before
  # any real player load.
  description = "Droplet size slug. 2GB dev floor; >=4GB for public play."
  type        = string
  default     = "s-1vcpu-2gb"
}

variable "droplet_image" {
  description = "Droplet base image (a plain Ubuntu LTS slug). cloud-init installs docker + the compose plugin, so no Marketplace Docker image is needed."
  type        = string
  default     = "ubuntu-24-04-x64"
}

variable "ssh_public_key" {
  description = "SSH public key material (contents, not a path) injected into the droplet."
  type        = string
}

variable "ssh_allowed_cidr" {
  description = "CIDR allowed to SSH (port 22). Default is open; lock to your IP/32."
  type        = string
  default     = "0.0.0.0/0"
}

variable "registry_name" {
  description = "DigitalOcean Container Registry name (globally unique)."
  type        = string
}

variable "registry_tier" {
  description = "DOCR subscription tier. We publish a single repository ('enb') with per-service version tags, so the free 'starter' tier (1 repo, 500 MiB) is enough; 'basic'+ only buys headroom."
  type        = string
  default     = "starter"
}

variable "manage_dns" {
  description = "If true, Terraform creates the Route53 A record. If false, point DNS yourself."
  type        = bool
  default     = true
}

variable "manage_cert" {
  description = "If true, Terraform issues+renews the Let's Encrypt cert via Route53 DNS-01."
  type        = bool
  default     = true
}

variable "acme_email" {
  description = "Contact email for the Let's Encrypt account."
  type        = string
  default     = ""
}

variable "acme_server_url" {
  description = "ACME directory URL. Default = Let's Encrypt production."
  type        = string
  default     = "https://acme-v02.api.letsencrypt.org/directory"
  # Staging (avoids rate limits while testing):
  # https://acme-staging-v02.api.letsencrypt.org/directory
}

# ---------------------------------------------------------------------------
# Phase AN: FreyaLauncher self-update delivery (private S3 + CloudFront + WAF).
# Entirely OPT-IN: with manage_patcher=false (the default) NONE of the patcher
# resources are created, so an existing deploy is untouched until the operator
# fills the new .env fields. See patcher.tf and the deploy README.
# ---------------------------------------------------------------------------
variable "manage_patcher" {
  description = "If true, Terraform creates the launcher-update delivery infra (private S3 bucket + CloudFront via OAC + ACM cert in us-east-1 + WAF rate rule + Route53 record). Default false = no patcher resources."
  type        = bool
  default     = false
}

variable "patcher_s3_bucket" {
  description = "Globally-unique name for the PRIVATE S3 bucket that holds the launcher artifacts + manifest.json. Required when manage_patcher=true."
  type        = string
  default     = ""
}

variable "patcher_dl_domain" {
  description = "Hostname CloudFront serves the artifacts on, e.g. dl.enb.sigsegv.land. Empty -> derived as dl.<domain_name>. Must live in the same Route53 zone."
  type        = string
  default     = ""
}

variable "patcher_rate_limit" {
  description = "WAFv2 per-source-IP request cap over a 5-minute window (floor 10). ~20 leaves headroom for a 3-file update + retries while still tripping a spam loop."
  type        = number
  default     = 20
}

# ---------------------------------------------------------------------------
# Phase AP: rolling Postgres -> private S3 backups (bucket + scoped IAM token).
# Entirely OPT-IN: with manage_db_backup=false (the default) NONE of the backup
# resources are created. The operator opts in by setting ENB_DB_BACKUP_S3_BUCKET
# in .env. See backup.tf and the deploy README.
# ---------------------------------------------------------------------------
variable "manage_db_backup" {
  description = "If true, Terraform creates the DB-backup infra (private S3 bucket + a bucket-scoped IAM user/token). Default false = no backup resources."
  type        = bool
  default     = false
}

variable "db_backup_s3_bucket" {
  description = "Globally-unique name for the PRIVATE S3 bucket that holds the Postgres dumps. Required when manage_db_backup=true."
  type        = string
  default     = ""
}

# NOTE: there is deliberately NO S3 lifecycle / expiry variable here. Retention
# is count-based and enforced by the sidecar (keep newest 24 hourly / 14 daily),
# never by a time-based S3 rule -- an age rule would keep deleting old dumps
# after the sidecar stops producing new ones and drain the backups to zero. See
# backup.tf for the full rationale. Dumps live under flat hourly/ and daily/
# prefixes; the sidecar's HOURLY_RETENTION / DAILY_RETENTION set the counts.
