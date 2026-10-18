terraform {
  # Values here cannot use variables — the backend is configured before anything
  # else is evaluated, so everything is written out literally.
  backend "s3" {
    bucket = "gameserver-tfstate-bobak"
    key    = "learn/terraform.tfstate"
    region = "eu-north-1"

    # State locking done by S3 itself. The older dynamodb_table argument is
    # deprecated since Terraform 1.11 and needs a table of its own.
    use_lockfile = true
  }
}
