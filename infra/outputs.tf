# Values coming out of the module are re-exported here, so the root keeps a single
# place to look regardless of which module produced what.

output "vpc_id" {
  description = "Id of the created VPC."
  value       = module.network.vpc_id
}

output "vpc_cidr" {
  description = "Address range assigned to the VPC."
  value       = module.network.vpc_cidr
}

output "public_subnet_id" {
  description = "Id of the public subnet."
  value       = module.network.public_subnet_id
}

output "availability_zone" {
  description = "Zone the subnet landed in — picked from a data source, not hardcoded."
  value       = module.network.availability_zone
}

output "instance_public_ip" {
  description = "Public address of the instance. It changes every time the instance is recreated."
  value       = aws_instance.app.public_ip
}

output "ssh_command" {
  description = "Ready to paste into PowerShell."
  value       = "ssh -i $env:USERPROFILE\\.ssh\\${var.key_pair_name}.pem ubuntu@${aws_instance.app.public_ip}"
}
