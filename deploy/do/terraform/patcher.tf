# ---------------------------------------------------------------------------
# Phase AN -- FreyaLauncher self-update delivery.
#
# The Windows FreyaLauncher SHA-512s its own FreyaLauncher.exe + FreyaProxy.exe
# at startup and POSTs both to the login server's /updateCheck. The login server
# answers from a manifest.json it GETs (credential-free) over CloudFront; the
# launcher then downloads any changed file from the SAME CloudFront host and
# self-replaces. This file stands up that delivery path:
#
#   private S3 bucket  --(OAC)-->  CloudFront  --(WAF rate rule)-->  internet
#                                      ^
#                                  ACM cert (us-east-1) + Route53 dl.<domain>
#
# The bucket is PRIVATE: only the CloudFront distribution can read it (Origin
# Access Control + a bucket policy scoped to this one distribution). Nothing is
# world-readable directly from S3.
#
# ENTIRELY OPT-IN: every resource is `count = var.manage_patcher ? 1 : 0`, so
# with the default manage_patcher=false this whole file is inert and an existing
# deploy is unaffected. The operator opts in by setting the patcher fields in
# .env (see .env.example) and re-running `just up`.
#
# Cost posture: one per-source-IP WAF rate rule (var.patcher_rate_limit req /
# 5 min, action block) caps a single abuser; CloudFront edge-caches repeat
# downloads so S3 GET/egress stays minimal. No Lambda@Edge, no per-file rules --
# the goal is "no surprise bill," not DDoS-grade protection (a distributed
# many-IP flood defeats any per-IP rule; add a global rate rule later if ever
# needed).
# ---------------------------------------------------------------------------

locals {
  patcher_enabled = var.manage_patcher
  # dl.<domain_name> unless an explicit host was given.
  patcher_dl_domain = var.patcher_dl_domain != "" ? var.patcher_dl_domain : "dl.${var.domain_name}"
}

# ---- private origin bucket ------------------------------------------------

resource "aws_s3_bucket" "patcher" {
  count  = local.patcher_enabled ? 1 : 0
  bucket = var.patcher_s3_bucket
}

# Block ALL public access -- the bucket is reachable only through CloudFront.
resource "aws_s3_bucket_public_access_block" "patcher" {
  count                   = local.patcher_enabled ? 1 : 0
  bucket                  = aws_s3_bucket.patcher[0].id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# Versioning suspended: the owner does not want to keep old launcher builds, and
# `just push` overwrites in place. The push/update race (a launcher mid-download
# during an overwrite) is handled by the launcher's hash verify -- it aborts and
# retries on next launch, never applies an inconsistent set. (See deploy README
# "launcher update race".)
resource "aws_s3_bucket_versioning" "patcher" {
  count  = local.patcher_enabled ? 1 : 0
  bucket = aws_s3_bucket.patcher[0].id
  versioning_configuration {
    status = "Suspended"
  }
}

# ---- CloudFront Origin Access Control (signs origin requests to S3) -------

resource "aws_cloudfront_origin_access_control" "patcher" {
  count                             = local.patcher_enabled ? 1 : 0
  name                              = "${var.project_name}-patcher-oac"
  description                       = "OAC for the FreyaLauncher update bucket"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

# ---- ACM cert for dl.<domain> (MUST be us-east-1 for CloudFront) ----------

resource "aws_acm_certificate" "patcher" {
  count             = local.patcher_enabled ? 1 : 0
  provider          = aws.us_east_1
  domain_name       = local.patcher_dl_domain
  validation_method = "DNS"

  lifecycle {
    create_before_destroy = true
  }
}

# DNS-01 validation records in the existing Route53 zone.
resource "aws_route53_record" "patcher_cert_validation" {
  for_each = local.patcher_enabled ? {
    for dvo in aws_acm_certificate.patcher[0].domain_validation_options :
    dvo.domain_name => {
      name   = dvo.resource_record_name
      type   = dvo.resource_record_type
      record = dvo.resource_record_value
    }
  } : {}

  zone_id = var.route53_zone_id
  name    = each.value.name
  type    = each.value.type
  ttl     = 60
  records = [each.value.record]

  allow_overwrite = true
}

resource "aws_acm_certificate_validation" "patcher" {
  count                   = local.patcher_enabled ? 1 : 0
  provider                = aws.us_east_1
  certificate_arn         = aws_acm_certificate.patcher[0].arn
  validation_record_fqdns = [for r in aws_route53_record.patcher_cert_validation : r.fqdn]
}

# ---- WAFv2 web ACL: one per-source-IP rate rule (CLOUDFRONT scope) --------

resource "aws_wafv2_web_acl" "patcher" {
  count    = local.patcher_enabled ? 1 : 0
  provider = aws.us_east_1
  name     = "${var.project_name}-patcher-waf"
  scope    = "CLOUDFRONT"

  default_action {
    allow {}
  }

  rule {
    name     = "per-ip-rate-limit"
    priority = 1

    action {
      block {}
    }

    statement {
      rate_based_statement {
        # AWS evaluates this over a rolling 5-minute window. Floor is 100 on
        # older API versions but 10 on current; we pass the operator's value
        # (default 20) -- a legit update is 1 manifest GET + up to 3 file GETs,
        # so 20 absorbs a couple of retries before blocking a spam loop.
        limit              = var.patcher_rate_limit
        aggregate_key_type = "IP"
      }
    }

    visibility_config {
      sampled_requests_enabled   = true
      cloudwatch_metrics_enabled = true
      metric_name                = "${var.project_name}-patcher-ratelimit"
    }
  }

  visibility_config {
    sampled_requests_enabled   = true
    cloudwatch_metrics_enabled = true
    metric_name                = "${var.project_name}-patcher-waf"
  }
}

# ---- CloudFront distribution ----------------------------------------------

resource "aws_cloudfront_distribution" "patcher" {
  count               = local.patcher_enabled ? 1 : 0
  enabled             = true
  is_ipv6_enabled     = true
  comment             = "FreyaLauncher self-update delivery (${local.patcher_dl_domain})"
  default_root_object = ""
  aliases             = [local.patcher_dl_domain]
  web_acl_id          = aws_wafv2_web_acl.patcher[0].arn
  price_class         = "PriceClass_100" # NA + EU edges; cheapest tier.

  origin {
    domain_name              = aws_s3_bucket.patcher[0].bucket_regional_domain_name
    origin_id                = "patcher-s3"
    origin_access_control_id = aws_cloudfront_origin_access_control.patcher[0].id
  }

  default_cache_behavior {
    target_origin_id       = "patcher-s3"
    viewer_protocol_policy = "https-only"
    allowed_methods        = ["GET", "HEAD"]
    cached_methods         = ["GET", "HEAD"]

    # AWS managed "CachingOptimized" policy. Repeat downloads serve from the
    # edge, not S3 -- this is what keeps egress cost down. `just push`
    # invalidates /* so a new build is picked up despite the cache.
    cache_policy_id = "658327ea-f89d-4fab-a63d-7e88639e58f6"
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    acm_certificate_arn      = aws_acm_certificate_validation.patcher[0].certificate_arn
    ssl_support_method       = "sni-only"
    minimum_protocol_version = "TLSv1.2_2021"
  }
}

# ---- bucket policy: only THIS distribution may read the bucket ------------

data "aws_iam_policy_document" "patcher_bucket" {
  count = local.patcher_enabled ? 1 : 0

  statement {
    sid       = "AllowCloudFrontServicePrincipalReadOnly"
    effect    = "Allow"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.patcher[0].arn}/*"]

    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "AWS:SourceArn"
      values   = [aws_cloudfront_distribution.patcher[0].arn]
    }
  }
}

resource "aws_s3_bucket_policy" "patcher" {
  count  = local.patcher_enabled ? 1 : 0
  bucket = aws_s3_bucket.patcher[0].id
  policy = data.aws_iam_policy_document.patcher_bucket[0].json
}

# ---- Route53 alias dl.<domain> -> the distribution ------------------------

resource "aws_route53_record" "patcher" {
  count   = local.patcher_enabled ? 1 : 0
  zone_id = var.route53_zone_id
  name    = local.patcher_dl_domain
  type    = "A"

  alias {
    name                   = aws_cloudfront_distribution.patcher[0].domain_name
    zone_id                = aws_cloudfront_distribution.patcher[0].hosted_zone_id
    evaluate_target_health = false
  }
}

resource "aws_route53_record" "patcher_aaaa" {
  count   = local.patcher_enabled ? 1 : 0
  zone_id = var.route53_zone_id
  name    = local.patcher_dl_domain
  type    = "AAAA"

  alias {
    name                   = aws_cloudfront_distribution.patcher[0].domain_name
    zone_id                = aws_cloudfront_distribution.patcher[0].hosted_zone_id
    evaluate_target_health = false
  }
}
