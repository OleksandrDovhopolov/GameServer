terraform {
  # Separate state: destroying the AWS learning stack in ../ never touches backups.
  backend "s3" {
    bucket       = "gameserver-tfstate-bobak"
    key          = "backup/terraform.tfstate"
    region       = "eu-north-1"
    use_lockfile = true
  }
}
