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

output "ssh_target" {
  description = "Where the deploy scripts SSH/scp the stack bundle."
  value       = "root@${digitalocean_reserved_ip.enb.ip_address}"
}

output "dns_status" {
  value = var.manage_dns ? "Terraform manages A ${var.domain_name} -> ${digitalocean_reserved_ip.enb.ip_address}" : "MANUAL: create A record ${var.domain_name} -> ${digitalocean_reserved_ip.enb.ip_address}"
}
