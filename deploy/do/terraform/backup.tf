# ---------------------------------------------------------------------------
# Phase AP -- rolling Postgres backups to a PRIVATE S3 bucket.
#
# The db-backup sidecar (db-backup/) dumps net7 + net7_user once an hour with
# `pg_dump -Fc`, uploads to s3://<bucket>/pg/hourly/<db>/<ts>.dump, promotes
# every 6th hour into pg/six-hourly/<db>/, and prunes by count. This file
# stands up the bucket and a BUCKET-SCOPED IAM token the sidecar uses.
#
#   db-backup sidecar --(scoped IAM token, write/list/delete)--> private S3 bucket
#
# The bucket is PRIVATE (all public access blocked) and is NEVER served to the
# internet -- unlike the patcher bucket there is no CloudFront in front of it.
# Only the IAM user created here can touch it, and only this one bucket.
#
# ENTIRELY OPT-IN: every resource is `count = var.manage_db_backup ? 1 : 0`, so
# with the default manage_db_backup=false this whole file is inert and an
# existing deploy is unaffected. The operator opts in by setting
# ENB_DB_BACKUP_S3_BUCKET in .env (see .env.example) and re-running `just up`,
# then copies the emitted access key/secret into the droplet env.
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

# Redundant defense-in-depth retention alongside the sidecar's count-based
# prune: expire hourly objects after 1 day and six-hourly objects after 8 days
# (24h + 7d). If the sidecar ever stops pruning (crash, S3 list throttle) the
# bucket still cannot grow without bound. Matches the sidecar key layout.
resource "aws_s3_bucket_lifecycle_configuration" "db_backup" {
  count  = local.db_backup_enabled ? 1 : 0
  bucket = aws_s3_bucket.db_backup[0].id

  rule {
    id     = "expire-hourly"
    status = "Enabled"
    filter {
      prefix = "${var.db_backup_s3_prefix}/hourly/"
    }
    expiration {
      days = var.db_backup_hourly_expire_days
    }
  }

  rule {
    id     = "expire-six-hourly"
    status = "Enabled"
    filter {
      prefix = "${var.db_backup_s3_prefix}/six-hourly/"
    }
    expiration {
      days = var.db_backup_six_hourly_expire_days
    }
  }
}

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
