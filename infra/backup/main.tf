terraform {
  required_version = ">= 1.5"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = "eu-north-1"

  default_tags {
    tags = {
      Project     = "gameserver"
      Environment = "staging"
      ManagedBy   = "terraform"
      Purpose     = "offsite-backup"
    }
  }
}

data "aws_caller_identity" "current" {}

resource "aws_s3_bucket" "backup" {
  bucket = "gameserver-staging-backups-${data.aws_caller_identity.current.account_id}"

  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_s3_bucket_public_access_block" "backup" {
  bucket                  = aws_s3_bucket.backup.id
  block_public_acls       = true
  ignore_public_acls      = true
  block_public_policy     = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_ownership_controls" "backup" {
  bucket = aws_s3_bucket.backup.id
  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "backup" {
  bucket = aws_s3_bucket.backup.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_versioning" "backup" {
  bucket = aws_s3_bucket.backup.id
  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "backup" {
  bucket = aws_s3_bucket.backup.id

  rule {
    id     = "expire-staging-backups"
    status = "Enabled"
    filter {
      prefix = "staging/"
    }
    expiration {
      days = 30
    }
    noncurrent_version_expiration {
      noncurrent_days = 30
    }
    abort_incomplete_multipart_upload {
      days_after_initiation = 7
    }
  }

  depends_on = [aws_s3_bucket_versioning.backup]
}

# Access keys are created manually, never by Terraform: Terraform state must not hold secrets.
resource "aws_iam_user" "uploader" {
  name = "gameserver-staging-backup-uploader"
}

resource "aws_iam_user_policy" "uploader" {
  name = "upload-staging-backups-only"
  user = aws_iam_user.uploader.name
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "s3:PutObject",
        "s3:AbortMultipartUpload"
      ]
      Resource = "${aws_s3_bucket.backup.arn}/staging/*"
    }]
  })
}

output "bucket_name" {
  value = aws_s3_bucket.backup.id
}

output "uploader_user" {
  value = aws_iam_user.uploader.name
}
