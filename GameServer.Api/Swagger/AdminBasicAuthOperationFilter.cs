using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameServer.Api.Swagger;

public sealed class AdminBasicAuthOperationFilter : IOperationFilter
{
    private const string SecuritySchemeName = "Basic";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var relativePath = context.ApiDescription.RelativePath ?? string.Empty;
        if (!relativePath.StartsWith("api/admin/", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(relativePath, "api/admin", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = SecuritySchemeName
                }
            }] = Array.Empty<string>()
        });

        operation.Responses.TryAdd("401", new OpenApiResponse
        {
            Description = "Unauthorized"
        });
    }
}
