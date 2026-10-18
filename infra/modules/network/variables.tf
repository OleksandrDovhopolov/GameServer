variable "name_prefix" {
  description = "Prefix for the Name tag of every resource in this module."
  type        = string
}

variable "vpc_cidr" {
  description = "Address range of the VPC."
  type        = string
}

variable "public_subnet_cidr" {
  description = "Address range of the public subnet. Must sit inside vpc_cidr."
  type        = string
}
