# DigitalOcean production deploy

Stand up the Earth & Beyond emulator on a DigitalOcean droplet, with the
domain in AWS Route53 and a Let's Encrypt cert, using PowerShell scripts over
Terraform. **The only manual step is pointing DNS** -- and even that is
automated if you let Terraform manage the Route53 record (the default).

This lives under `deploy/do/` and is completely separate from the dev files in
`deploy/` (`Net7Config.cfg`, `certs/`), which the repo-root `docker-compose.yml`
uses for local development. Nothing here touches those.

## Quick update -- the one command

To deploy a new build, run this from `deploy/do/`:

```
just update
```

That is the whole thing. `just update` is end-to-end: it builds + pushes the
server-side images (and, when the patcher is configured, the Windows client
patch), then ships everything to the droplet and recreates the changed
containers. The lower-level steps it chains (`push-server`, `push-client`,
`push`, `apply-update`) are listed in [Day-to-day](#day-to-day) for when you
want just one of them.

## What it builds

```
            AWS Route53                     DigitalOcean
   A enb.sigsegv.land -> reserved IP  ->  [ droplet, docker compose ]
                                            |- freya-online :443  (TLS; website + relays auth to net7go)
                                            |- net7go  (game-auth, internal :8085 plain HTTP)
                                            |- server  :3501-3800/3806/3808/3810 udp
                                            |- postgres (internal only)
   images pulled from  ->  DigitalOcean Container Registry (private)
   terraform state in  ->  AWS S3 (versioned, encrypted, locked)
   TLS cert from       ->  Let's Encrypt (DNS-01 via Route53)

   The proxy is NOT here. It is a per-client, single-connection bridge (one
   proxy per player), so each player runs their own on their Windows machine;
   it dials freya-online :443 + the server UDP planes above.
```

One droplet runs the server-side stack via `docker compose`. Images are built
locally and pushed to a private **DigitalOcean Container Registry** (yes, DO
has one); the droplet pulls from it. The two server-side services (server +
login) share a **single repository** (`enb`) with per-service version tags, so
the deploy fits DOCR's **free Starter tier** (1 repository, 500 MiB) -- see
"Image registry layout" below. Terraform state lives in an **S3 bucket**, never
on your machine (it holds the LE private key).

## Prerequisites (on the machine you deploy from)

- PowerShell 7+ (`pwsh`)
- Terraform >= 1.10
- Docker with `buildx` (to build the linux/amd64 images)
- OpenSSH `ssh` + `scp` (ship the stack to the droplet)
- AWS CLI (only for the one-time state-bucket bootstrap)
- A DigitalOcean API token, a configured **AWS CLI v2 profile** (set
  `AWS_PROFILE` in `.env` -- no standing keys live in the file) with Route53
  access on your zone **plus IAM user management** (Terraform creates a scoped
  renewal user -- needs `iam:CreateUser` / `CreateAccessKey` / `PutUserPolicy`
  and the matching `Delete*` for teardown; drop the IAM perms only if you run
  `MANAGE_CERT=false` and supply the cert yourself), and an SSH keypair.

## Setup

```powershell
cd deploy/do
cp .env.example .env        # then edit .env -- every value is documented inline
```

`.env` is gitignored. Fill in the DO token, your `AWS_PROFILE` name + Route53
zone id, your domain (`enb.sigsegv.land`), a globally-unique registry name and
S3 bucket name, and your SSH key paths.

## Deploy (first time)

```powershell
just bootstrap   # one-time: create the encrypted S3 state bucket
just up          # PLAN ONLY: prints what infra would change, applies nothing
just up -y       # actually converge: droplet, IP, firewall, registry, DNS, TLS
just update      # build + push images, then ship config/certs/seed data + start the stack
```

`just up` is a dry run by default -- it prints the terraform plan and applies
nothing, so you always see a droplet REPLACEMENT (which wipes the droplet-local
DB) before it happens. Add `-y` to apply.

(No `just`? Call the scripts directly: `./scripts/Bootstrap-State.ps1`,
`./scripts/Deploy-Infra.ps1`, etc.)

If you set `MANAGE_DNS=true` (default), the A record is created for you -- there
are **zero manual steps**. If you set it false, `just up` prints the reserved IP;
create `A enb.sigsegv.land -> <that IP>` in Route53 yourself. The reserved IP is
stable across droplet rebuilds, so you set DNS once.

## Day-to-day

| Command | What it does |
|---|---|
| `just take-backup` | `pg_dumpall` both DBs from the live droplet into the gitignored `backup/enb-backup-<UTC-ts>.sql`. Runs automatically before `up` and `apply-update`; skips cleanly if no droplet is deployed yet. |
| `just restore-backup <file>` | **DESTRUCTIVE.** scp a local `take-backup` dump to the droplet, drop + recreate `net7` and `net7_user`, and reload them from the dump. Asks to confirm. Used to seed the durable volume during migration, or for disaster recovery. |
| `just up` | DRY RUN -- print the terraform plan, apply nothing. (Backs up the DB first.) |
| `just up -y` | Converge infra. Idempotent. Issues the bootstrap cert; the droplet auto-renews thereafter (see below). (Backs up the DB first -- a droplet REPLACE wipes the local pgdata volume.) |
| `just update [tag]` | **THE full deploy.** Chains `just push` (build + push server images, and the client patch when configured) then `just apply-update` (ship + restart). This is all you normally run. |
| `just push-server [vN]` | Build + push the server-side services (server + net7go + freya-online + status-notifier + db-backup) into repo `enb` as `*-vN` (default: auto-increment), re-point `*-latest`, prune to the newest 3 versions/service, GC. |
| `just push-client` | (Phase AN, opt-in) Build the Windows launcher bundle, write `manifest.json`, upload all of it to the patcher S3 bucket, and invalidate CloudFront. Errors out unless the patcher is configured (see "Launcher self-update"). |
| `just push [tag]` | `push-server` + (when the patcher is configured) `push-client`. Build + push everything, but does NOT ship to the droplet. |
| `just apply-update [tag]` | Pull images + recreate changed containers on the droplet. The ship half of `update`; does NOT build or push. (Backs up the DB first.) |
| `just start` | Start an already-deployed stack (no pull). |
| `just stop` | Stop containers; droplet, DB volume, and infra stay. |
| `just destroy` | Tear down ALL infra incl. the droplet + its DB volume. Asks to confirm. |

Ship a new build: just `just update`. **Cert renewal is automatic** -- the
droplet renews itself (see below); you do not need to run anything on a
schedule.

### SSH host keys

Terraform treats the droplet as cattle: changing the image/size (or any
droplet-forcing field) REPLACES it with a fresh box that has a new SSH host
key, while the reserved IP stays the same. Pinning that key in your global
`~/.ssh/known_hosts` would then break every rebuild with "REMOTE HOST
IDENTIFICATION HAS CHANGED". So the deploy scripts deliberately pass
`-o UserKnownHostsFile=/dev/null -o StrictHostKeyChecking=accept-new` -- host
keys are never written to your known_hosts and never conflict on a rebuild.

Trade-off: this drops MITM protection on the admin SSH channel (which carries
root + the DB password), trusting the network path to the reserved IP. That is
acceptable for an own-the-box, frequently-rebuilt droplet. If you want real
verification, generate the droplet's SSH host key in terraform (a `tls_private_key`
written to `/etc/ssh/ssh_host_ed25519_key` via cloud-init) and pin its public
half in a deploy-local known_hosts file -- then the key is stable across rebuilds
and verifiable. Not wired up yet; ask if you want it.

## Admin (over SSH)

Run from `deploy/do/`; both reach the droplet via SSH using the same key as the
deploy. Scripts live in `deploy/do/tools/`.

| Command | What it does |
|---|---|
| `just create-account <user> <pass>` | Create a player account in `net7_user.accounts`. Password is Argon2id-hashed **locally** (needs `python3-nacl`); only the hash crosses the wire. Refuses if the username already exists -- it will NOT overwrite a live account. |
| `just get-server-status` | Container health + uptime, players online, and the sectors they occupy. |

"Players online" is the server's own definition: an account with
`last_login > last_logout` in `net7_user` (the server resets these on boot and
maintains them on login/logout). "Sectors occupied" is the distinct set of
sectors those online players are in -- sectors are started on demand and tracked
only in server memory, so an idle-but-loaded sector does not appear.

Creating characters is still done from the game client after the account
exists; there is no server-side character creation path.

## Image registry layout

DOCR's free **Starter** tier allows exactly **one repository** (and 500 MiB).
A naive deploy wants a separate image name per service = a repo per service =
paid Basic tier. To stay free, all server-side services live in a **single
repository** named `enb`, distinguished by tag prefix:

```
enb:server-vN  enb:net7go-vN  enb:freya-online-vN  enb:status-notifier-vN      <- immutable versioned
enb:{server,net7go,freya-online,status-notifier}-latest                       <- alias, re-pointed at vN
```

`status-notifier` is the Phase AM sidecar: a Discord bot that posts the
external-status outbox into a channel and serves the `/status` + `/notify` slash
commands (see `docs/18-external-status-events.md`). It is default-off; the bot runs
when `DISCORD_BOT_TOKEN` is set in the droplet `.env`, and the relay delivers when
`STATUS_CHANNEL_ID` is also set (admins toggle individual kinds with `/notify`).

The proxy is **not** in the registry at all -- it is a per-client bridge that
runs on each player's Windows machine and ships with the client package, never
server-side. The server-side stack is ~50 MiB of dedup'd, compressed layers --
nowhere near the 500 MiB cap, so storage was never the constraint; the repo
count was.

`just push-server` (`Build-And-Push.ps1`) builds + pushes only the server-side
images. (`just push` is `push-server` plus the client patch when the patcher is
configured; `just update` chains `push` then `apply-update` -- see "Day to day"
above.) The server-side push:

1. Reads existing tags, finds the highest `vN`, builds `v(N+1)` (or the `-Tag`
   you pass). All services share one monotonic counter -- they bump together.
2. Builds + pushes each `enb:<svc>-v(N+1)` for `linux/amd64`.
3. **Only after every versioned push succeeds**, re-points `enb:<svc>-latest`
   at the new version with `docker buildx imagetools create` (an alias of the
   already-pushed manifest -- no rebuild, no extra storage).
4. Prunes to the **newest 3 versions per service**, deleting older version tags
   via the DO registry API, then triggers a **garbage-collection** to reclaim
   the now-untagged blobs (deletion alone only untags; GC frees the bytes).

`just apply-update [vN|latest]` (and the `apply-update` leg of `just update`)
ships whichever tag suffix you name (default `latest`); the compose file resolves
`enb:server-<tag>` / `enb:net7go-<tag>` / `enb:freya-online-<tag>` / `enb:status-notifier-<tag>`.

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
   `/opt/enb/certs/`, and restarts **only** the `freya-online` container (the
   TLS terminator) so it re-reads the cert (loaded at startup, no hot-reload).
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

## Durable database storage (block volume)

`pgdata` does **not** live on the droplet's ephemeral disk. It lives on a
separate **DigitalOcean block volume** (`<project>-pgdata`, default 10 GiB)
that terraform creates and attaches. This is the whole point of the volume:
terraform REPLACES the droplet whenever `user_data` changes -- and it changes on
its own, because the registry docker credentials baked into cloud-init rotate
every `apply`. A replace gives you a brand-new box with a blank disk. With
`pgdata` on the droplet's disk that would silently wipe the database on a routine
`just update`. On the block volume, the volume just detaches from the old box and
re-attaches to the new one; the data is untouched.

How it fits together:

- **terraform** (`main.tf`): `digitalocean_volume.pgdata` (pre-formatted ext4,
  `prevent_destroy = true` so `just destroy` cannot take the DB with it) +
  `digitalocean_volume_attachment.pgdata`. Toggle with `var.manage_db_volume`
  (default on); size with `var.db_volume_size_gb`.
- **cloud-init** (`mount-data-volume.sh`): on every boot, waits for the attached
  device, `mkfs.ext4` only if it is blank (never reformats existing data),
  mounts it at `/mnt/enb-data` via a UUID fstab entry (`nofail`), and ensures
  `/mnt/enb-data/pgdata` exists. With no managed volume it falls back to a
  `pgdata` dir on the root disk and exits 0.
- **docker compose** (`docker-compose.prod.yml`): the `pgdata` named volume is a
  bind to `/mnt/enb-data/pgdata`, so Postgres writes straight onto the block
  volume.

The backup mechanisms are unaffected by the move: `take-backup`, the on-box
`db-backup.sh` timer, and the S3 `db-backup` sidecar all dump over a TCP
connection to the postgres container, so where `pgdata` physically sits is
irrelevant to them.

### Migrating the LIVE droplet onto the volume (one-time cutover)

The droplet that predates this change still has its DB on the root disk. Moving
it onto the volume means a droplet replace, so the database has to be carried
across by hand via a dump/restore. The volume comes up empty; the first boot
seeds the default content DB; then you restore your real data over that seed.
Run these from `deploy/do/`, in order:

```pwsh
# 1. Snapshot the CURRENT live DB to a local file. (up/apply-update also do this
#    automatically, but take it explicitly so you know the exact filename.)
just take-backup
#    -> writes backup/enb-backup-<UTC-ts>.sql ; note the filename.

# 2. Apply infra. This CREATES the volume and REPLACES the droplet; cloud-init
#    formats + mounts the (empty) volume at /mnt/enb-data. Review the plan first:
just up          # dry run -- confirm it shows the volume being created
just up -y       # apply

# 3. Ship the stack onto the new droplet. The app comes up against the empty
#    volume and schema-init SEEDS fresh default databases. This also pushes the
#    new db-backup S3 sidecar image.
just update

# 4. Overwrite the seed with your real data from step 1.
just restore-backup backup/enb-backup-<UTC-ts>.sql   # type 'restore' to confirm

# 5. Sanity check.
just get-server-status
```

After this, every later `just update` that replaces the droplet keeps the DB:
the volume re-attaches, cloud-init mounts the existing filesystem (no reformat),
and Postgres opens the existing `pgdata`. No restore needed again -- steps 1 and
4 are the one-time migration, not part of routine deploys.

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

**Scope:** this is on-box logical backup -- it protects against bad migrations
and accidental data loss. It is on the droplet's own root disk, so the *dumps*
themselves do **not** survive losing the droplet. Note this is now separate from
DB durability: `pgdata` itself lives on a block volume that DOES survive droplet
replacement (see "Durable database storage" above), so a routine `just update`
no longer threatens the database. For off-box copies of the *dumps*, the S3
`db-backup` sidecar rolls hourly `pg_dump -Fc` archives into a private bucket
(default-off; set `BACKUP_S3_BUCKET`), or sync `/opt/enb/backups` to Spaces.

## Ports opened

From `common/include/net7/Ports.h`:

- `443/tcp` -- auth TLS (login-server)
- `3501-3800, 3806, 3808, 3810/udp` -- server UDP planes (sector/MVAS/master/global)
- `22/tcp` -- SSH, restricted to `SSH_ALLOWED_CIDR` (lock to your IP)

The proxy's client-facing TCP ports (3500/3801/3805) are **not** opened here:
the proxy runs on each player's own Windows machine, so those listeners live on
the player's box. Each player's local proxy dials freya-online `443` (TLS auth,
relayed to net7go) + the server UDP planes above, which are the only inbound
game ports the cloud exposes.

## Honest caveats (read before trusting a public deploy)

- **Game traffic is CLEARTEXT, over the public internet.** Only the 443 auth
  leg is encrypted. Each player's local proxy strips the client's RC4 and sends
  cleartext UDP (positions, chat, inventory, combat) to the droplet -- so with
  the proxy on the player's machine, that cleartext now crosses the open
  internet, not just a localhost hop. TLS/Let's Encrypt does nothing for it.
  Anyone on-path can read or tamper with gameplay traffic. If you want it
  encrypted, see `plans/35-phase-ah-dtls-proxy-server.md` (approval-gated) or
  front it with WireGuard.
- **Remote addressing is UNVERIFIED.** `Net7Config.cfg`'s `internal_ip` is set
  to the droplet's public reserved IP at deploy time (the most-likely-correct
  value), but the in-game server->client handoff (ServerRedirect / sector IP)
  has not been confirmed against the real Win32 client over the internet. A
  remote login may need config tuning + a real-client test before it fully
  works. This is the load-bearing unknown for public play; do not assume it
  works until you've tested with `client.exe`.
- **Cost.** Default is a **$12/mo droplet (`s-1vcpu-2gb`)** -- the dev/single-
  tester floor (stack idles ~1.5GB, leaving ~500MiB headroom). Bump
  `DROPLET_SIZE=s-2vcpu-4gb` (~$24/mo) before real player load. The registry is
  **free** (Starter tier -- single repo, ~50 MiB of images well under the 500
  MiB cap), plus a reserved IP (free while attached) + trivial S3.
- **The database survives droplet replacement; the on-box dumps do not.**
  `pgdata` lives on a durable block volume that re-attaches across replaces (see
  "Durable database storage"), so `just update` no longer risks the DB. The
  6-hourly on-box `pg_dump` into `/opt/enb/backups` is on the droplet's root disk
  and is lost with the droplet -- for off-box dump copies, enable the S3
  `db-backup` sidecar (`BACKUP_S3_BUCKET`) or sync the dir to Spaces.

## Launcher self-update (Phase AN, opt-in)

The Windows **FreyaLauncher** can keep itself and the bundled **FreyaProxy.exe**
current without the player re-running the installer. At startup it SHA-512s its
own `FreyaLauncher.exe` and `bin/FreyaProxy.exe` and POSTs both hashes to
`/updateCheck` (terminated by freya-online, relayed to net7go). net7go compares
them against a
`manifest.json` it fetched at boot and replies `UP_TO_DATE` or a list of changed
files + a base URL. The launcher downloads each changed file from that URL,
verifies its hash, and self-replaces (rename-self-then-relaunch). `FreyaLauncher.cfg`
has no independent hash -- it rides along whenever the launcher EXE changes.

Delivery is a **private S3 bucket fronted by CloudFront** (Origin Access Control,
so nothing is world-readable from S3 directly), with an ACM cert + Route53
`dl.<domain>` alias and a single per-source-IP WAF rate rule to cap abuse. net7go
reads `manifest.json` credential-free over that same CloudFront host.

**Entirely opt-in and OFF by default.** With `ENB_PATCHER_PRIVATE_S3_BUCKET`
blank (the default), `TF_VAR_manage_patcher=false`: terraform creates none of the
S3/CloudFront/ACM/WAF resources, the net7go container gets empty
`NET7_PATCHER_*` env, its manifest never loads, and `/updateCheck` fail-closes
(reports the server DOWN to the launcher -- harmless, the launcher just doesn't
self-update). An existing deploy is untouched until you opt in.

To turn it on:

1. Set `ENB_PATCHER_PRIVATE_S3_BUCKET` (a globally-unique bucket name) in `.env`,
   optionally `PATCHER_DL_DOMAIN` (defaults to `dl.<DOMAIN_NAME>`) and
   `PATCHER_RATE_LIMIT` (default 20). See `.env.example`.
2. `just up -y` -- stands up the bucket + CloudFront + cert + WAF + DNS. (The ACM
   cert validates via the same Route53 zone as the main cert; first CloudFront
   propagation can take ~15 min.)
3. `just update` -- now that the patcher is configured, the `push` leg also runs
   `push-client` (builds the Windows launcher bundle via `package-client-windows`,
   writes `manifest.json` over the three artifacts, uploads all four to the bucket,
   invalidates CloudFront), and the `apply-update` leg threads
   `NET7_PATCHER_MANIFEST_URL` / `NET7_PATCHER_DL_BASE` (derived from the patcher
   outputs) into the droplet `.env` and restarts login -- which then loads the
   freshly-uploaded manifest at boot. The combined order (push, then ship) is why
   one `just update` is sufficient: artifacts + manifest land before login
   re-reads them.

For every later launcher/proxy build, `just update` again re-pushes the client
patch and restarts login. To push only the client patch without touching the
server stack, run `just push-client` on its own, then `just apply-update` so login
re-reads the manifest.

**Update race.** The bucket is unversioned and `just push-client` overwrites in
place. A launcher mid-download during an overwrite is covered by the launcher's
post-download hash verify: a mismatched/partial file is rejected and retried on
the next launch, so an inconsistent set is never applied. The cost is one wasted
launch, not a broken client.

**Order matters:** artifacts + manifest must be in the bucket before login
re-reads them. `just update` gets this right by construction (its `push` leg runs
before its `apply-update` leg). Only if you drive the legs by hand does ordering
bite: run `push-client` (or `push`) before `apply-update`. An `apply-update`
against a fresh, empty bucket 404s the manifest GET and `/updateCheck` reports
DOWN until the artifacts land -- no breakage, just no self-update in that window.

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
