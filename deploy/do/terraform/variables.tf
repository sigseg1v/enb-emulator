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
  description = "Droplet size slug. The stack (Postgres + 3 C++ services) wants >=4GB."
  type        = string
  default     = "s-2vcpu-4gb"
}

variable "droplet_image" {
  description = "Droplet base image. The DO Marketplace Docker image ships docker + compose."
  type        = string
  default     = "docker-20-04"
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
  description = "DOCR subscription tier. 'starter' allows 1 repo; we push 3 images, so 'basic'+."
  type        = string
  default     = "basic"
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
