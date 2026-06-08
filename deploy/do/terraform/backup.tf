# ---------------------------------------------------------------------------
# Phase AP -- rolling Postgres backups to a PRIVATE S3 bucket.
#
# The db-backup sidecar (db-backup/) dumps net7 + net7_user once an hour with
# `pg_dump -Fc`, uploads to s3://<bucket>/hourly/<db>/<ts>.dump, promotes once a
# day into daily/<db>/, and prunes BY COUNT (keep newest 24 hourly / 14 daily).
# This file stands up the bucket and a BUCKET-SCOPED IAM token the sidecar uses.
#
#   db-backup sidecar --(scoped IAM token, write/list/delete)--> private S3 bucket
#
# The bucket is PRIVATE (all public access blocked) and is NEVER served to the
# internet -- unlike the patcher bucket there is no CloudFront in front of it.
# Only the IAM user created here can touch it, and only this one bucket.
#
# NO S3 lifecycle / time-based expiry policy is set, deliberately. S3 lifecycle
# can ONLY expire objects by age, not by count -- and an age-based rule would
# keep deleting old dumps even after the sidecar stops producing new ones, i.e.
# it would drain the backups to ZERO during exactly the outage when you need
# them most. Retention is therefore COUNT-based and lives only in the sidecar's
# prune, which deletes an old dump only when a fresh one has replaced it. A
# stalled sidecar freezes the set instead of emptying it.
#
# ENTIRELY OPT-IN: every resource is `count = var.manage_db_backup ? 1 : 0`, so
# with the default manage_db_backup=false this whole file is inert and an
# existing deploy is unaffected. The operator opts in by setting just
# ENB_DB_BACKUP_S3_BUCKET in .env (see .env.example) and re-running `just up`.
# The emitted access key/secret are NOT pasted anywhere: they live in the
# tfstate, and Update-Stack.ps1 reads them straight from the tf outputs
# (db_backup_access_key_id / db_backup_secret_access_key) into the droplet env.
# ---------------------------------------------------------------------------

locals {
  db_backup_enabled = var.manage_db_backup
}

# ---- private backup bucket ------------------------------------------------

resource "aws_s3_bucket" "db_backup" {
  count  = local.db_backup_enabled ? 1 : 0
  bucket = var.db_backup_s3_bucket
}

# Block ALL public access -- backups are private, never world-readable.
resource "aws_s3_bucket_public_access_block" "db_backup" {
  count                   = local.db_backup_enabled ? 1 : 0
  bucket                  = aws_s3_bucket.db_backup[0].id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# Encrypt at rest with SSE-S3 (AES256). No customer-managed key needed for a
# backup bucket; this just ensures objects are not stored in the clear.
resource "aws_s3_bucket_server_side_encryption_configuration" "db_backup" {
  count  = local.db_backup_enabled ? 1 : 0
  bucket = aws_s3_bucket.db_backup[0].id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

# (No aws_s3_bucket_lifecycle_configuration here, on purpose -- see the file
# header. Retention is count-based in the sidecar, never time-based in S3.)

# ---- bucket-scoped IAM token ----------------------------------------------
# A dedicated IAM user whose ONLY permission is read/write/list/delete on THIS
# bucket. The sidecar uses this user's access key. No console access, no other
# AWS API. This is the "special s3 bucket scoped token" the owner asked for.

resource "aws_iam_user" "db_backup" {
  count = local.db_backup_enabled ? 1 : 0
  name  = "${var.project_name}-db-backup"
  tags = {
    project = var.project_name
    purpose = "rolling postgres backups -> private S3 (Phase AP)"
  }
}

resource "aws_iam_user_policy" "db_backup" {
  count = local.db_backup_enabled ? 1 : 0
  name  = "${var.project_name}-db-backup-bucket-scope"
  user  = aws_iam_user.db_backup[0].name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid      = "ListThisBucketOnly"
        Effect   = "Allow"
        Action   = ["s3:ListBucket", "s3:GetBucketLocation"]
        Resource = [aws_s3_bucket.db_backup[0].arn]
      },
      {
        Sid      = "ReadWriteDeleteObjectsInThisBucketOnly"
        Effect   = "Allow"
        Action   = ["s3:PutObject", "s3:GetObject", "s3:DeleteObject"]
        Resource = ["${aws_s3_bucket.db_backup[0].arn}/*"]
      }
    ]
  })
}

resource "aws_iam_access_key" "db_backup" {
  count = local.db_backup_enabled ? 1 : 0
  user  = aws_iam_user.db_backup[0].name
}

# ---- outputs (consumed by the operator, threaded into the droplet env) -----
# The secret is sensitive: `terraform output -raw db_backup_secret_access_key`.

output "db_backup_bucket" {
  description = "Name of the private S3 backup bucket (empty when manage_db_backup=false)."
  value       = local.db_backup_enabled ? aws_s3_bucket.db_backup[0].bucket : ""
}

output "db_backup_access_key_id" {
  description = "Access key id for the bucket-scoped backup IAM user."
  value       = local.db_backup_enabled ? aws_iam_access_key.db_backup[0].id : ""
}

output "db_backup_secret_access_key" {
  description = "Secret access key for the bucket-scoped backup IAM user."
  value       = local.db_backup_enabled ? aws_iam_access_key.db_backup[0].secret : ""
  sensitive   = true
}
