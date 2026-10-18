variable "region" {
  description = "AWS region everything is created in. One region only — resources in another region stay invisible in the console but still bill."
  type        = string
  default     = "eu-north-1"
}

variable "name_prefix" {
  description = "Prefix for the Name tag of every resource."
  type        = string
  default     = "learn"
}

variable "vpc_cidr" {
  description = "Address range of the VPC."
  type        = string
  default     = "10.0.0.0/16"
}

variable "public_subnet_cidr" {
  description = "Address range of the public subnet. Must sit inside vpc_cidr."
  type        = string
  default     = "10.0.1.0/24"
}

# No default on purpose: a forgotten default is how SSH ends up open to 0.0.0.0/0.
# Set it in terraform.tfvars, which is gitignored.
variable "allowed_ssh_cidr" {
  description = "The single address allowed to reach SSH, as a /32. Find yours with: curl ifconfig.me"
  type        = string

  validation {
    condition     = can(cidrhost(var.allowed_ssh_cidr, 0)) && var.allowed_ssh_cidr != "0.0.0.0/0"
    error_message = "Must be a valid CIDR and must not be 0.0.0.0/0."
  }
}

variable "key_pair_name" {
  description = "Name of an existing EC2 key pair used for SSH."
  type        = string
  default     = "ServerKP"
}

variable "instance_type" {
  description = "EC2 instance type. This is the one resource here that costs money."
  type        = string
  default     = "t3.micro"
}
