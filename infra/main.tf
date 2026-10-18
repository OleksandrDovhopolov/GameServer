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
  region = var.region

  # Every resource created here gets these tags, including everything inside
  # modules, so учебное can be told apart from anything else in the account.
  default_tags {
    tags = {
      Project     = "gameserver"
      Environment = "learning"
      ManagedBy   = "terraform"
    }
  }
}

# The whole network is now one call. Its internals — subnet, gateway, route
# table, association — are not visible from here, only its inputs and outputs.
module "network" {
  source = "./modules/network"

  name_prefix        = var.name_prefix
  vpc_cidr           = var.vpc_cidr
  public_subnet_cidr = var.public_subnet_cidr
}

data "aws_ami" "ubuntu" {
  most_recent = true
  owners      = ["099720109477"] # Canonical

  filter {
    name   = "name"
    values = ["ubuntu/images/hvm-ssd*/ubuntu-noble-24.04-amd64-server-*"]
  }
}

resource "aws_security_group" "ssh" {
  name        = "${var.name_prefix}-ssh"
  description = "SSH from a single address"
  vpc_id      = module.network.vpc_id

  ingress {
    description = "SSH"
    from_port   = 22
    to_port     = 22
    protocol    = "tcp"
    cidr_blocks = [var.allowed_ssh_cidr]
  }

  # AWS allows all outbound traffic by default, but Terraform does not: leave this
  # block out and the group ends up with no egress at all, so apt would hang.
  egress {
    description = "All outbound"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "${var.name_prefix}-ssh"
  }
}

# The only resource here that is billed. Destroy it when the session ends.
resource "aws_instance" "app" {
  ami                    = data.aws_ami.ubuntu.id
  instance_type          = var.instance_type
  subnet_id              = module.network.public_subnet_id
  vpc_security_group_ids = [aws_security_group.ssh.id]
  key_name               = var.key_pair_name

  tags = {
    Name = "${var.name_prefix}-app"
  }
}
