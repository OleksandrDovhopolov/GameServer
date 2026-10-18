# Outputs are the module's public interface. Anything not listed here stays
# invisible to whoever calls the module.

output "vpc_id" {
  description = "Id of the VPC."
  value       = aws_vpc.this.id
}

output "vpc_cidr" {
  description = "Address range assigned to the VPC."
  value       = aws_vpc.this.cidr_block
}

output "public_subnet_id" {
  description = "Id of the public subnet, for placing instances into."
  value       = aws_subnet.public.id
}

output "availability_zone" {
  description = "Zone the subnet landed in."
  value       = aws_subnet.public.availability_zone
}
