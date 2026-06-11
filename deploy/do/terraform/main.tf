# ---------------------------------------------------------------------------
# Earth & Beyond emulator -- DigitalOcean production infrastructure.
#
#   - 1 droplet running the docker-compose stack (server + login + postgres),
#     images pulled from a private DigitalOcean Container Registry. The proxy
#     is per-client and runs on each player's own machine, not here.
#   - 1 reserved IP so the address survives droplet rebuilds; DNS points here.
#   - 1 cloud firewall opening exactly the game/auth ports.
#   - (optional) Route53 A record + a Let's Encrypt cert via DNS-01.
#
# Everything is idempotent: re-running `apply` converges, it does not stack up.
# ---------------------------------------------------------------------------

# SSH key the operator uses to drive the droplet (deploy bundle + compose).
resource "digitalocean_ssh_key" "enb" {
  name       = "${var.project_name}-deploy"
  public_key = var.ssh_public_key
}

# Private image registry. The droplet pulls enb:server-* / enb:net7go-* /
# enb:freya-online-* from here; the operator pushes to it
# (scripts/Build-And-Push.ps1).
resource "digitalocean_container_registry" "enb" {
  name                   = var.registry_name
  subscription_tier_slug = var.registry_tier
}

# Read-only docker credentials baked into the droplet so `docker compose pull`
# can authenticate to the private registry without a token at runtime.
resource "digitalocean_container_registry_docker_credentials" "enb" {
  registry_name = digitalocean_container_registry.enb.name
  write         = false
}

# ---------------------------------------------------------------------------
# Durable Postgres storage. pgdata lives on THIS block volume, not the droplet's
# ephemeral disk, so a droplet REPLACE (forced whenever user_data changes -- the
# registry docker credentials rotate on their own) does NOT wipe the database.
# The volume detaches from the old droplet and re-attaches to the replacement;
# cloud-init mounts it at /mnt/enb-data WITHOUT reformatting (it only mkfs's a
# blank device), and the prod compose binds the pgdata volume to
# /mnt/enb-data/pgdata. initial_filesystem_type pre-formats it once at creation.
#
# prevent_destroy guards the DB: terraform will REFUSE to delete this volume
# (so `just destroy` cannot silently take the database with it). Remove the
# volume by hand in the DO console when you genuinely mean to discard the DB.
resource "digitalocean_volume" "pgdata" {
  count                   = var.manage_db_volume ? 1 : 0
  region                  = var.droplet_region
  name                    = "${var.project_name}-pgdata"
  size                    = var.db_volume_size_gb
  initial_filesystem_type = "ext4"
  description             = "Durable Postgres pgdata for ${var.project_name} (survives droplet replacement)."

  lifecycle {
    prevent_destroy = true
  }
}

resource "digitalocean_volume_attachment" "pgdata" {
  count      = var.manage_db_volume ? 1 : 0
  droplet_id = digitalocean_droplet.enb.id
  volume_id  = digitalocean_volume.pgdata[0].id
}

resource "digitalocean_droplet" "enb" {
  name     = var.project_name
  region   = var.droplet_region
  size     = var.droplet_size
  image    = var.droplet_image
  ssh_keys = [digitalocean_ssh_key.enb.fingerprint]

  user_data = templatefile("${path.module}/cloud-init.yaml.tftpl", {
    docker_credentials    = digitalocean_container_registry_docker_credentials.enb.docker_credentials
    manage_cert           = var.manage_cert
    domain_name           = var.domain_name
    acme_email            = var.acme_email
    acme_server_url       = var.acme_server_url
    route53_zone_id       = var.route53_zone_id
    aws_region            = var.aws_region
    cert_renew_access_key = var.manage_cert ? aws_iam_access_key.cert_renew[0].id : ""
    cert_renew_secret_key = var.manage_cert ? aws_iam_access_key.cert_renew[0].secret : ""
    # Empty string => no managed volume; cloud-init keeps pgdata on the root disk.
    db_volume_name        = var.manage_db_volume ? "${var.project_name}-pgdata" : ""
  })

  # Re-provision the droplet if the registry creds rotate.
  lifecycle {
    create_before_destroy = false
  }
}

# Stable public address. DNS points at this, not at the droplet's ephemeral IP.
resource "digitalocean_reserved_ip" "enb" {
  region = var.droplet_region
}

resource "digitalocean_reserved_ip_assignment" "enb" {
  ip_address = digitalocean_reserved_ip.enb.ip_address
  droplet_id = digitalocean_droplet.enb.id
}

# ---------------------------------------------------------------------------
# Firewall. Port list is the protocol's, from common/include/net7/Ports.h:
#   443/tcp         auth TLS (freya-online terminates + relays to net7go; the ONLY TLS leg)
#   3501-3800/udp   per-sector UDP planes
#   3806/udp        MVAS position channel (MVAS_LOGIN_PORT)
#   3808/udp        master UDP (UDP_MASTER_SERVER_PORT)
#   3810/udp        global UDP (UDP_GLOBAL_SERVER_PORT)
#
# The proxy is NOT deployed here: it is a per-client, single-connection bridge
# (one proxy per player), so it cannot be shared by a cloud and runs on each
# player's own Windows machine instead. The player's local proxy dials login
# 443 + these UDP planes, so those are the only inbound game ports the cloud
# needs. The proxy's own client-facing TCP listeners (3500/3801/3805) live on
# the player's box, never here. NOTE: game UDP is CLEARTEXT on the wire by
# design (see README).
# ---------------------------------------------------------------------------
resource "digitalocean_firewall" "enb" {
  name        = "${var.project_name}-fw"
  droplet_ids = [digitalocean_droplet.enb.id]

  inbound_rule {
    protocol         = "tcp"
    port_range       = "22"
    source_addresses = [var.ssh_allowed_cidr]
  }

  inbound_rule {
    protocol         = "tcp"
    port_range       = "443"
    source_addresses = ["0.0.0.0/0", "::/0"]
  }

  # Server UDP planes (sector range + master/global/MVAS). The player's local
  # proxy dials these directly; no server-side proxy TCP listeners are opened.
  dynamic "inbound_rule" {
    for_each = ["3501-3800", "3806", "3808", "3810"]
    content {
      protocol         = "udp"
      port_range       = inbound_rule.value
      source_addresses = ["0.0.0.0/0", "::/0"]
    }
  }

  # Egress: allow all (image pulls, DNS, ACME, outbound game traffic).
  outbound_rule {
    protocol              = "tcp"
    port_range            = "1-65535"
    destination_addresses = ["0.0.0.0/0", "::/0"]
  }
  outbound_rule {
    protocol              = "udp"
    port_range            = "1-65535"
    destination_addresses = ["0.0.0.0/0", "::/0"]
  }
  outbound_rule {
    protocol              = "icmp"
    destination_addresses = ["0.0.0.0/0", "::/0"]
  }
}

# Tidy grouping in the DO console.
resource "digitalocean_project" "enb" {
  name        = var.project_name
  description = "Earth & Beyond emulator preservation server."
  purpose     = "Service or API"
  environment = "Production"
  resources   = [digitalocean_droplet.enb.urn]
}

# ---------------------------------------------------------------------------
# Route53 A record (optional). With a reserved IP the value is stable.
# ---------------------------------------------------------------------------
resource "aws_route53_record" "enb" {
  count   = var.manage_dns ? 1 : 0
  zone_id = var.route53_zone_id
  name    = var.domain_name
  type    = "A"
  ttl     = 300
  records = [digitalocean_reserved_ip.enb.ip_address]
}

# ---------------------------------------------------------------------------
# Let's Encrypt certificate via Route53 DNS-01 (optional).
#
# DNS-01 is used (not HTTP-01) because the auth port 443 is the Westwood SSL
# listener, not an ACME-capable web server -- and DNS-01 needs no inbound
# port 80. The cert is written to ../certs-prod/<domain>.cer (fullchain) and
# <domain>.pem (key), which Update-Stack.ps1 ships to the droplet.
#
# Renewal: re-running `apply` (Deploy-Infra.ps1) re-issues when the cert is
# within ~30 days of expiry. The LE private key is stored in tfstate -- which
# is why state lives in an encrypted S3 bucket, never locally.
# ---------------------------------------------------------------------------
resource "tls_private_key" "acme_account" {
  count     = var.manage_cert ? 1 : 0
  algorithm = "RSA"
  rsa_bits  = 4096
}

resource "acme_registration" "this" {
  count           = var.manage_cert ? 1 : 0
  account_key_pem = tls_private_key.acme_account[0].private_key_pem
  email_address   = var.acme_email
}

resource "acme_certificate" "this" {
  count           = var.manage_cert ? 1 : 0
  account_key_pem = acme_registration.this[0].account_key_pem
  common_name     = var.domain_name

  dns_challenge {
    provider = "route53"
    # AWS creds come from the environment. Pin the zone so the resolver does
    # not need ListHostedZones permission.
    config = {
      AWS_HOSTED_ZONE_ID = var.route53_zone_id
    }
  }
}

# <domain>.cer = leaf + issuer chain (PEM). SSL_Listener.cpp currently loads
# leaf-only, but shipping the fullchain is forward-safe (see README caveat).
resource "local_sensitive_file" "cert_cer" {
  count           = var.manage_cert ? 1 : 0
  filename        = "${path.module}/../certs-prod/${var.domain_name}.cer"
  file_permission = "0600"
  content         = "${acme_certificate.this[0].certificate_pem}${acme_certificate.this[0].issuer_pem}"
}

# <domain>.pem = private key (PEM).
resource "local_sensitive_file" "cert_key" {
  count           = var.manage_cert ? 1 : 0
  filename        = "${path.module}/../certs-prod/${var.domain_name}.pem"
  file_permission = "0600"
  content         = acme_certificate.this[0].private_key_pem
}

# ---------------------------------------------------------------------------
# Autonomous renewal credentials.
#
# Terraform issues the FIRST cert (above) so the very first deploy boots with a
# valid cert. Thereafter the droplet renews itself on a daily systemd timer
# (cloud-init installs lego + the timer) -- so the cert lifecycle does NOT
# depend on anyone re-running `just up` from a workstation.
#
# That means the droplet needs Route53 write access for the DNS-01 challenge.
# This IAM user is least-privilege: it can ONLY touch records in exactly the
# one hosted zone (and read change status). It cannot list other zones, touch
# other AWS services, or escalate. The access key lands on the droplet via
# cloud-init user_data (root-only file) and is stored in the encrypted S3
# tfstate -- treat both as sensitive.
# ---------------------------------------------------------------------------
resource "aws_iam_user" "cert_renew" {
  count = var.manage_cert ? 1 : 0
  name  = "${var.project_name}-cert-renew"
}

resource "aws_iam_access_key" "cert_renew" {
  count = var.manage_cert ? 1 : 0
  user  = aws_iam_user.cert_renew[0].name
}

resource "aws_iam_user_policy" "cert_renew" {
  count = var.manage_cert ? 1 : 0
  name  = "route53-dns01"
  user  = aws_iam_user.cert_renew[0].name
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid      = "GetChangeStatus"
        Effect   = "Allow"
        Action   = "route53:GetChange"
        Resource = "arn:aws:route53:::change/*"
      },
      {
        Sid    = "EditOneZone"
        Effect = "Allow"
        Action = [
          "route53:GetHostedZone",
          "route53:ListResourceRecordSets",
          "route53:ChangeResourceRecordSets",
        ]
        Resource = "arn:aws:route53:::hostedzone/${var.route53_zone_id}"
      },
    ]
  })
}
