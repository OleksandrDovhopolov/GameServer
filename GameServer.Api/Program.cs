using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using GameServer.Api.Configuration;
using GameServer.Api.Repositories;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Filters;
using GameServer.Api.Swagger;
using Dapper;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Basic", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "basic",
        In = ParameterLocation.Header,
        Description = "Basic Authentication header. Format: Basic base64(username:password)"
    });
    options.OperationFilter<AdminBasicAuthOperationFilter>();
});
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod |
                            HttpLoggingFields.RequestPath |
                            HttpLoggingFields.ResponseStatusCode |
                            HttpLoggingFields.Duration;
});

builder.Services.AddScoped<IPackSelectionStrategy, PackSelectionStrategy>();
builder.Services.AddScoped<IPackService, PackService>();
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection(RedisOptions.SectionName));
builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection(PostgresOptions.SectionName));
builder.Services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
{
    var redisOptions = serviceProvider.GetRequiredService<IOptions<RedisOptions>>().Value;
    if (string.IsNullOrWhiteSpace(redisOptions.ConnectionString))
    {
        throw new InvalidOperationException("Redis:ConnectionString is not configured.");
    }

    return ConnectionMultiplexer.Connect(redisOptions.ConnectionString);
});
builder.Services.AddScoped<ISaveRepository, RedisSaveRepository>();
builder.Services.AddScoped<IPlayerAggregateRepository, RedisPlayerAggregateRepository>();
builder.Services.AddScoped<IPlayerResourcesRepository, RedisPlayerResourcesRepository>();
builder.Services.AddScoped<IPlayerInventoryRepository, RedisPlayerInventoryRepository>();
builder.Services.AddScoped<IGameSaveAssembler, GameSaveAssembler>();
builder.Services.AddScoped<ISaveService, SaveService>();
builder.Services.AddScoped<IResourceAdjustmentService, ResourceAdjustmentService>();
builder.Services.AddScoped<IInventoryManagementService, InventoryManagementService>();
builder.Services.AddSingleton<IRewardDefinitionRepository, RewardDefinitionRepository>();
builder.Services.AddScoped<IRewardPartApplier, RewardPartApplier>();
builder.Services.AddScoped<IRewardHandler, ResourceRewardHandler>();
builder.Services.AddScoped<IRewardHandler, InventoryRewardHandler>();
builder.Services.AddScoped<IRewardApplicationService, RewardApplicationService>();
builder.Services.Configure<AdminAuthOptions>(builder.Configuration.GetSection("AdminAuth"));
builder.Services.AddScoped<AdminBasicAuthFilter>();
builder.Services.AddSingleton<IRandomGenerator, RandomGenerator>();
builder.Services.AddSingleton<ISystemClock, SystemClock>();
builder.Services.AddSingleton<IWheelRewardCatalog, WheelRewardCatalog>();
builder.Services.AddScoped<IWheelService, WheelService>();

var app = builder.Build();

// Force reward catalog initialization at startup so invalid configuration fails fast.
app.Services.GetRequiredService<IWheelRewardCatalog>();
app.Services.GetRequiredService<IRewardDefinitionRepository>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpLogging();

app.MapControllers();

app.MapGet("/ping", () => "pong");

app.Run();
