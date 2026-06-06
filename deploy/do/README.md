# DigitalOcean production deploy

Stand up the Earth & Beyond emulator on a DigitalOcean droplet, with the
domain in AWS Route53 and a Let's Encrypt cert, using PowerShell scripts over
Terraform. **The only manual step is pointing DNS** -- and even that is
automated if you let Terraform manage the Route53 record (the default).

This lives under `deploy/do/` and is completely separate from the dev files in
`deploy/` (`Net7Config.cfg`, `certs/`), which the repo-root `docker-compose.yml`
uses for local development. Nothing here touches those.

## What it builds

```
            AWS Route53                     DigitalOcean
   A enb.sigsegv.land -> reserved IP  ->  [ droplet, docker compose ]
                                            |- login   :443  (TLS auth)
                                            |- proxy   :3500/3801/3805 tcp
                                            |- server  :3501-3800/3806/3808/3810 udp
                                            |- postgres (internal only)
   images pulled from  ->  DigitalOcean Container Registry (private)
   terraform state in  ->  AWS S3 (versioned, encrypted, locked)
   TLS cert from       ->  Let's Encrypt (DNS-01 via Route53)
```

One droplet runs the whole stack via `docker compose`. Images are built locally
and pushed to a private **DigitalOcean Container Registry** (yes, DO has one);
the droplet pulls from it. Terraform state lives in an **S3 bucket**, never on
your machine (it holds the LE private key).

## Prerequisites (on the machine you deploy from)

- PowerShell 7+ (`pwsh`)
- Terraform >= 1.10
- Docker with `buildx` (to build the linux/amd64 images)
- OpenSSH `ssh` + `scp` (ship the stack to the droplet)
- AWS CLI (only for the one-time state-bucket bootstrap)
- A DigitalOcean API token, AWS creds with Route53 access on your zone **plus
  IAM user management** (Terraform creates a scoped renewal user -- needs
  `iam:CreateUser` / `CreateAccessKey` / `PutUserPolicy` and the matching
  `Delete*` for teardown), and an SSH keypair.

## Setup

```powershell
cd deploy/do
cp .env.example .env        # then edit .env -- every value is documented inline
```

`.env` is gitignored. Fill in the DO token, AWS creds + Route53 zone id, your
domain (`enb.sigsegv.land`), a globally-unique registry name and S3 bucket name,
and your SSH key paths.

## Deploy (first time)

```powershell
just bootstrap   # one-time: create the encrypted S3 state bucket
just up          # infra: droplet, IP, firewall, registry, DNS record, TLS cert
just push        # build + push enb-server / enb-login / enb-proxy to the registry
just update      # ship config/certs/seed data, pull images, start the stack
```

(No `just`? Call the scripts directly: `./scripts/Bootstrap-State.ps1`,
`./scripts/Deploy-Infra.ps1`, etc.)

If you set `MANAGE_DNS=true` (default), the A record is created for you -- there
are **zero manual steps**. If you set it false, `just up` prints the reserved IP;
create `A enb.sigsegv.land -> <that IP>` in Route53 yourself. The reserved IP is
stable across droplet rebuilds, so you set DNS once.

## Day-to-day

| Command | What it does |
|---|---|
| `just up` | Converge infra. Idempotent. Issues the bootstrap cert; the droplet auto-renews thereafter (see below). |
| `just push [tag]` | Build + push the 3 images (default tag = short git SHA). |
| `just update [tag]` | Pull images + recreate changed containers on the droplet. |
| `just start` | Start an already-deployed stack (no pull). |
| `just stop` | Stop containers; droplet, DB volume, and infra stay. |
| `just destroy` | Tear down ALL infra incl. the droplet + its DB volume. Asks to confirm. |

Ship a new build: `just push` then `just update`. **Cert renewal is
automatic** -- the droplet renews itself (see below); you do not need to run
anything on a schedule.

## HTTPS / the certificate

Only the **auth leg (login-server :443)** is TLS. The player's launcher does a
full .NET certificate-chain + hostname check against it, so a self-signed cert
fails -- **Let's Encrypt is genuinely required here.**

The cert is issued via **DNS-01 through Route53** (not HTTP-01: port 443 runs
the Westwood SSL listener, not an ACME web server, and DNS-01 needs no inbound
port 80).

**Account registration:** Terraform registers a Let's Encrypt account
(`acme_registration` -- a 4096-bit account key + your `ACME_EMAIL`) once, stored
in tfstate. The droplet's renewer registers its own account on first renewal.

**Lifecycle (two stages, by design):**

1. **Bootstrap (Terraform).** `just up` issues the first cert via the `acme`
   provider, writes `certs-prod/<domain>.cer` (fullchain) + `<domain>.pem`
   (key), and the first `just update` ships them to the droplet, where the
   server reads `<domain>.cer`/`.pem` from its CWD (`SSL_Listener.cpp`). This
   makes the very first boot work immediately.
2. **Renewal (the droplet, autonomously).** cloud-init installs a `lego`
   container on a **daily systemd timer** (`enb-cert-renew.timer`). It renews
   in place within 30 days of expiry -- a fresh DNS-01 challenge each time
   (ACME always revalidates on renewal) -- writes the new cert to
   `/opt/enb/certs/`, and restarts **only** the `login` container so it
   re-reads the cert (`SSL_Listener.cpp` loads at startup, no hot-reload).
   **No workstation involvement** -- renewal does not depend on anyone running
   `just up` again. After bootstrap, `just update` detects the droplet already
   has a cert and stops shipping the local copy, so it never clobbers a
   droplet-renewed cert with a stale one.

For the renewal the droplet needs Route53 write access, so Terraform creates a
**least-privilege IAM user** (`<project>-cert-renew`) scoped to exactly
`ChangeResourceRecordSets` / `ListResourceRecordSets` / `GetHostedZone` on your
one hosted zone plus `GetChange` -- nothing else. Its access key lands on the
droplet via cloud-init (root-only `/opt/enb/cert-renew/lego.env`) and is in
tfstate. Both are sensitive; the encrypted S3 state bucket and the firewalled
droplet are why that is acceptable, but the blast radius if the droplet is
popped is "can edit DNS in that one zone."

Inspect/trigger renewal on the droplet:

```bash
systemctl list-timers enb-cert-renew.timer
journalctl -u enb-cert-renew.service
systemctl start enb-cert-renew.service   # force a renewal check now
```

The LE private key (bootstrap) is stored in Terraform state -- another reason
state lives in an encrypted S3 bucket. Set `MANAGE_CERT=false` to skip the
whole cert apparatus (bootstrap issuance, the IAM user, and the droplet timer)
and supply certs yourself.

**Caveat (leaf-only load):** `SSL_Listener.cpp` uses
`SSL_CTX_use_certificate_file`, which sends only the leaf cert, not the
intermediate chain. .NET's `SslStream` usually rebuilds the LE chain from the
Windows cert store, so it normally works -- but if you see cert-chain errors
from the launcher, the fix is a one-line server change to
`SSL_CTX_use_certificate_chain_file` (we already ship the fullchain in `.cer`).
That is a server change governed by the repo's server-integrity rules; it is not
applied automatically.

## Database backups

The droplet runs a `pg_dump` of both databases (`net7` content + `net7_user`
save-state) **every 6 hours**, driven by a systemd timer (`enb-db-backup.timer`,
installed by cloud-init). Dumps land gzip'd in the host directory
**`/opt/enb/backups`** as `net7-<ts>.sql.gz` / `net7_user-<ts>.sql.gz`, and
anything older than **7 days** is pruned. The job uses the postgres container's
own credentials and skips cleanly if the stack isn't up yet.

```bash
ls -la /opt/enb/backups                  # see the dumps
systemctl list-timers enb-db-backup.timer
systemctl start enb-db-backup.service    # force a backup now
journalctl -u enb-db-backup.service      # logs

# restore one DB (DESTRUCTIVE -- drops + recreates the target):
gunzip -c /opt/enb/backups/net7_user-<ts>.sql.gz \
  | docker exec -i $(docker compose --env-file /opt/enb/.env \
      -f /opt/enb/docker-compose.prod.yml ps -q postgres) \
      psql -U net7 -d net7_user
```

**Scope:** this is on-box logical backup -- it protects against bad migrations,
a corrupt `pgdata` volume, and accidental data loss. It is on the droplet's own
disk, so it does **not** survive losing the droplet (e.g. `just destroy`, or any
Terraform change that replaces the droplet -- `user_data` edits do). For
droplet-loss durability, sync `/opt/enb/backups` to DO Spaces / S3 or turn on DO
droplet backups.

## Ports opened

From `common/include/net7/Ports.h`:

- `443/tcp` -- auth TLS (login-server)
- `3500, 3801, 3805/tcp` -- server-side proxy listeners
- `3501-3800, 3806, 3808, 3810/udp` -- server UDP planes (sector/MVAS/master/global)
- `22/tcp` -- SSH, restricted to `SSH_ALLOWED_CIDR` (lock to your IP)

Both the proxy TCP ports and the server UDP ports are opened because the shipped
Windows package runs a **client-side** proxy (which dials the UDP planes),
while the dev/server-side-proxy model uses the TCP ports. Opening both keeps
either topology working; tighten the firewall once you've settled on one.

## Honest caveats (read before trusting a public deploy)

- **Game traffic is CLEARTEXT.** Only the 443 auth leg is encrypted. The
  proxy<->server UDP (positions, chat, inventory, combat) is unencrypted on the
  wire by design -- the Net7 proxy strips the client's RC4 and forwards
  cleartext. TLS/Let's Encrypt does nothing for gameplay traffic. If you want
  that encrypted, see `plans/35-phase-ah-dtls-proxy-server.md` (approval-gated)
  or front it with WireGuard.
- **Remote addressing is UNVERIFIED.** `Net7Config.cfg`'s `internal_ip` is set
  to the droplet's public reserved IP at deploy time (the most-likely-correct
  value), but the in-game server->client handoff (ServerRedirect / sector IP)
  has not been confirmed against the real Win32 client over the internet. A
  remote login may need config tuning + a real-client test before it fully
  works. This is the load-bearing unknown for public play; do not assume it
  works until you've tested with `client.exe`.
- **Cost.** Default is a **$12/mo droplet (`s-1vcpu-2gb`)** -- the dev/single-
  tester floor (stack idles ~1.5GB, leaving ~500MiB headroom). Bump
  `DROPLET_SIZE=s-2vcpu-4gb` (~$24/mo) before real player load. Plus ~$5/mo
  registry (basic) + a reserved IP (free while attached) + trivial S3.
- **DB backups are on-box only.** The 6-hourly `pg_dump` (below) lands in
  `/opt/enb/backups` on the droplet -- it survives bad migrations / a corrupt
  pgdata volume / accidental wipes, but NOT droplet loss (same disk). For
  droplet-loss durability, copy the dumps off-box (Spaces/S3) or enable DO
  droplet backups.

## tfstate storage

State is in S3 (`TFSTATE_BUCKET`/`TFSTATE_KEY`), created by `just bootstrap`
with versioning + AES256 encryption + public-access-block, and locked natively
(Terraform 1.10 `use_lockfile`, no DynamoDB needed). It is never written to your
local disk. `just destroy` leaves the bucket intact.

## Files

```
deploy/do/
  README.md                     this file
  .env.example                  copy to .env, fill in
  justfile                      deploy recipes (separate from repo-root justfile)
  terraform/                    droplet, IP, firewall, DOCR, Route53 record, ACME cert
    cloud-init.yaml.tftpl       droplet bootstrap (registry creds + dirs)
  compose/
    docker-compose.prod.yml     prod stack (images from DOCR; login on 443:443)
    Net7Config.cfg              template; __DOMAIN__/__INTERNAL_IP__ filled per deploy
  scripts/                      *.ps1 -- Bootstrap/Deploy/Build-And-Push/Update/Start/Stop/Destroy
  certs-prod/                   (gitignored) LE cert written here by terraform
```
