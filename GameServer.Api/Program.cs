using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using GameServer.Api.Configuration;
using GameServer.Api.Repositories;
using GameServer.Api.Repositories.Interfaces;
using Dapper;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Microsoft.AspNetCore.HttpLogging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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
builder.Services.AddScoped<RedisSaveRepository>();
builder.Services.AddScoped<PostgreSqlSaveRepository>();
builder.Services.AddScoped<ISaveRepository, HybridSaveRepository>();
builder.Services.AddScoped<ISaveService, SaveService>();
builder.Services.AddSingleton<IRandomGenerator, RandomGenerator>();
builder.Services.AddSingleton<ISystemClock, SystemClock>();
builder.Services.AddSingleton<IWheelRewardCatalog, WheelRewardCatalog>();
builder.Services.AddScoped<IWheelService, WheelService>();

var app = builder.Build();

// PostgreSQL schema initialization is temporarily disabled (Redis-only persistence).
/*
var stoppingToken = app.Lifetime.ApplicationStopping;
await using (var scope = app.Services.CreateAsyncScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup.Postgres");
    var postgresOptions = scope.ServiceProvider.GetRequiredService<IOptions<PostgresOptions>>().Value;
    if (string.IsNullOrWhiteSpace(postgresOptions.ConnectionString))
    {
        throw new InvalidOperationException("Postgres:ConnectionString is not configured.");
    }

    startupLogger.LogInformation("Starting PostgreSQL schema initialization.");
    using var conn = new NpgsqlConnection(postgresOptions.ConnectionString);
    await conn.OpenAsync(stoppingToken);
    startupLogger.LogInformation("PostgreSQL connection opened for schema initialization.");

    await conn.ExecuteAsync(new CommandDefinition(
        @"CREATE TABLE IF NOT EXISTS player_saves (
            player_id VARCHAR(128) PRIMARY KEY,
            save_data TEXT NOT NULL,
            last_modified_at BIGINT NOT NULL,
            updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );",
        cancellationToken: stoppingToken));

    await conn.ExecuteAsync(new CommandDefinition(
        @"CREATE INDEX IF NOT EXISTS idx_player_saves_last_modified
          ON player_saves(last_modified_at);",
        cancellationToken: stoppingToken));
    startupLogger.LogInformation("PostgreSQL schema initialization completed successfully.");
}
*/

// Force reward catalog initialization at startup so invalid configuration fails fast.
app.Services.GetRequiredService<IWheelRewardCatalog>();

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpLogging();

app.MapControllers();

app.MapGet("/ping", () => "pong");

app.Run();
