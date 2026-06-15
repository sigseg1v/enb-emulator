output "reserved_ip" {
  description = "Stable public IP. Point your DNS A record here (if not managed)."
  value       = digitalocean_reserved_ip.enb.ip_address
}

output "registry_endpoint" {
  description = "Full registry path for image tags, e.g. registry.digitalocean.com/<name>."
  value       = digitalocean_container_registry.enb.endpoint
}

output "registry_name" {
  value = digitalocean_container_registry.enb.name
}

output "droplet_id" {
  value = digitalocean_droplet.enb.id
}

output "db_volume" {
  description = "Durable pgdata block-volume name (empty when manage_db_volume=false)."
  value       = var.manage_db_volume ? digitalocean_volume.pgdata[0].name : ""
}

output "ssh_target" {
  description = "Where the deploy scripts SSH/scp the stack bundle."
  value       = "root@${digitalocean_reserved_ip.enb.ip_address}"
}

output "dns_status" {
  value = var.manage_dns ? "Terraform manages A ${var.domain_name} -> ${digitalocean_reserved_ip.enb.ip_address}" : "MANUAL: create A record ${var.domain_name} -> ${digitalocean_reserved_ip.enb.ip_address}"
}

# ---- Phase AN launcher-update delivery (only when manage_patcher=true) -----

output "patcher_s3_bucket" {
  description = "Private S3 bucket holding the launcher artifacts + manifest.json (empty when manage_patcher=false)."
  value       = var.manage_patcher ? aws_s3_bucket.patcher[0].bucket : ""
}

output "patcher_cloudfront_id" {
  description = "CloudFront distribution id (for `aws cloudfront create-invalidation`)."
  value       = var.manage_patcher ? aws_cloudfront_distribution.patcher[0].id : ""
}

output "patcher_dl_base" {
  description = "Base URL the launcher + login server use for artifacts/manifest, e.g. https://dl.<domain>."
  value       = var.manage_patcher ? "https://${local.patcher_dl_domain}" : ""
}

output "patcher_manifest_url" {
  description = "The manifest.json URL freya-online GETs at startup (FREYA_PATCHER_MANIFEST_URL)."
  value       = var.manage_patcher ? "https://${local.patcher_dl_domain}/manifest.json" : ""
}
