using System.Reflection;
using System.Text.Json;
using GameServer.Api.Swagger;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class AdminBasicAuthOperationFilterTests
{
    [Fact]
    public void Apply_AdminRoute_AddsBasicSecurityAnd401Response()
    {
        var filter = new AdminBasicAuthOperationFilter();
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses()
        };
        var context = CreateContext("api/admin/player/{id}");

        filter.Apply(operation, context);

        Assert.NotNull(operation.Security);
        Assert.Single(operation.Security!);
        Assert.True(operation.Responses.ContainsKey("401"));
    }

    [Fact]
    public void Apply_NonAdminRoute_DoesNotAddSecurity()
    {
        var filter = new AdminBasicAuthOperationFilter();
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses()
        };
        var context = CreateContext("api/v1/wheel/data");

        filter.Apply(operation, context);

        Assert.True(operation.Security is null || operation.Security.Count == 0);
        Assert.False(operation.Responses.ContainsKey("401"));
    }

    [Fact]
    public void Apply_V1AdminRoute_DoesNotAddSecurity()
    {
        var filter = new AdminBasicAuthOperationFilter();
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses()
        };
        var context = CreateContext("api/v1/admin/configs/books");

        filter.Apply(operation, context);

        Assert.True(operation.Security is null || operation.Security.Count == 0);
        Assert.False(operation.Responses.ContainsKey("401"));
    }

    private static OperationFilterContext CreateContext(string relativePath)
    {
        var apiDescription = new ApiDescription
        {
            RelativePath = relativePath
        };
        var schemaGenerator = new SchemaGenerator(
            new SchemaGeneratorOptions(),
            new JsonSerializerDataContractResolver(new JsonSerializerOptions()));
        var schemaRepository = new SchemaRepository();
        var methodInfo = typeof(AdminBasicAuthOperationFilterTests)
            .GetMethod(nameof(DummyMethod), BindingFlags.NonPublic | BindingFlags.Static)!;

        return new OperationFilterContext(
            apiDescription,
            schemaGenerator,
            schemaRepository,
            methodInfo);
    }

    private static void DummyMethod()
    {
    }
}
