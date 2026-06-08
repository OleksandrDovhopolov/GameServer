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
builder.Services.Configure<AdminToolsOptions>(builder.Configuration.GetSection(AdminToolsOptions.SectionName));
builder.Services.Configure<GooglePlayBillingOptions>(builder.Configuration.GetSection(GooglePlayBillingOptions.SectionName));

var googlePlayBillingOptions = builder.Configuration.GetSection(GooglePlayBillingOptions.SectionName).Get<GooglePlayBillingOptions>() ?? new GooglePlayBillingOptions();
LogGooglePlayBillingConfigurationState(builder.Environment, googlePlayBillingOptions);
ValidateGooglePlayBillingConfiguration(builder.Environment, googlePlayBillingOptions);

builder.Services.AddSingleton(sp =>
{
    var postgresOptions = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
    if (string.IsNullOrWhiteSpace(postgresOptions.ConnectionString))
        throw new InvalidOperationException("Postgres:ConnectionString is not configured.");
    return Npgsql.NpgsqlDataSource.Create(postgresOptions.ConnectionString);
});
builder.Services.AddScoped<IConfigRepository, PostgresConfigRepository>();
builder.Services.AddScoped<IConfigService, ConfigService>();

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
builder.Services.AddScoped<IRewardHandler, FreeSpinRewardHandler>();
builder.Services.AddScoped<IRewardApplicationService, RewardApplicationService>();
builder.Services.AddScoped<IPlayerEconomyRepository, RedisPlayerEconomyRepository>();
builder.Services.AddScoped<IRewardIntentRepository, RedisRewardIntentRepository>();
builder.Services.AddScoped<IRewardIntentService, RewardIntentService>();
builder.Services.AddScoped<IDistributedLockService, RedisDistributedLockService>();
builder.Services.AddScoped<IRedisAdminClient, RedisAdminClient>();
builder.Services.AddScoped<IAdminMaintenanceService, AdminMaintenanceService>();
builder.Services.AddScoped<IBattlePassSeasonRepository, RedisBattlePassSeasonRepository>();
builder.Services.AddScoped<IBattlePassStateRepository, RedisBattlePassStateRepository>();
builder.Services.AddScoped<IEntitlementRepository, RedisEntitlementRepository>();
builder.Services.AddScoped<IPurchaseRepository, RedisPurchaseRepository>();
builder.Services.AddScoped<IProductCatalogRepository, RedisProductCatalogRepository>();
builder.Services.AddScoped<IEntitlementService, EntitlementService>();
builder.Services.AddScoped<ILiveOpsScheduleItemProvider, BattlePassLiveOpsProvider>();
builder.Services.AddScoped<ILiveOpsScheduleService, FileLiveOpsScheduleService>();
builder.Services.AddScoped<IBattlePassService, BattlePassService>();
builder.Services.AddScoped<IIapService, IapService>();
builder.Services.AddScoped<IBattlePassAdminService, BattlePassAdminService>();
if (googlePlayBillingOptions.Enabled)
{
    builder.Services.AddSingleton<IGooglePlayProductPurchaseClient, GooglePlayPublisherApiClient>();
    builder.Services.AddScoped<IGooglePlayPurchaseVerifier, GooglePlayPurchaseVerifier>();
}
else
{
    builder.Services.AddScoped<IGooglePlayPurchaseVerifier, StubGooglePlayPurchaseVerifier>();
}
builder.Services.Configure<AdminAuthOptions>(builder.Configuration.GetSection("AdminAuth"));
builder.Services.AddScoped<AdminBasicAuthFilter>();
builder.Services.AddSingleton<IRandomGenerator, RandomGenerator>();
builder.Services.AddSingleton<ISystemClock, SystemClock>();
builder.Services.AddSingleton<IWheelRewardCatalog, WheelRewardCatalog>();
builder.Services.AddScoped<IWheelService, WheelService>();

var app = builder.Build();

app.Logger.LogInformation(
    "Google Play billing startup state. Enabled: {Enabled}, PackageNameSet: {PackageNameSet}, ServiceAccountJsonSet: {ServiceAccountJsonSet}, ServiceAccountKeyFileSet: {ServiceAccountKeyFileSet}, ApplicationName: {ApplicationName}, HttpTimeoutSeconds: {HttpTimeoutSeconds}, VerifierMode: {VerifierMode}",
    googlePlayBillingOptions.Enabled,
    !string.IsNullOrWhiteSpace(googlePlayBillingOptions.PackageName),
    !string.IsNullOrWhiteSpace(googlePlayBillingOptions.ServiceAccountJson),
    !string.IsNullOrWhiteSpace(googlePlayBillingOptions.ServiceAccountKeyFile),
    googlePlayBillingOptions.ApplicationName,
    googlePlayBillingOptions.HttpTimeoutSeconds,
    googlePlayBillingOptions.Enabled ? nameof(GooglePlayPurchaseVerifier) : nameof(StubGooglePlayPurchaseVerifier));

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

static void ValidateGooglePlayBillingConfiguration(IWebHostEnvironment environment, GooglePlayBillingOptions options)
{
    if (!options.Enabled)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("GooglePlayBilling must be enabled outside Development.");
        }

        return;
    }

    if (string.IsNullOrWhiteSpace(options.PackageName))
    {
        throw new InvalidOperationException("GooglePlayBilling:PackageName is not configured.");
    }

    var hasServiceAccountJson = !string.IsNullOrWhiteSpace(options.ServiceAccountJson);
    var hasServiceAccountKeyFile = !string.IsNullOrWhiteSpace(options.ServiceAccountKeyFile);

    if (!hasServiceAccountJson && !hasServiceAccountKeyFile)
    {
        throw new InvalidOperationException("GooglePlayBilling requires either ServiceAccountJson or ServiceAccountKeyFile.");
    }

    if (hasServiceAccountKeyFile && !File.Exists(options.ServiceAccountKeyFile))
    {
        throw new InvalidOperationException($"GooglePlayBilling:ServiceAccountKeyFile was not found at '{options.ServiceAccountKeyFile}'.");
    }
}

static void LogGooglePlayBillingConfigurationState(IWebHostEnvironment environment, GooglePlayBillingOptions options)
{
    Console.WriteLine(
        "GooglePlayBilling config state: Environment={0}; Enabled={1}; PackageNameSet={2}; ServiceAccountJsonLength={3}; ServiceAccountKeyFileLength={4}",
        environment.EnvironmentName,
        options.Enabled,
        !string.IsNullOrWhiteSpace(options.PackageName),
        options.ServiceAccountJson?.Length ?? 0,
        options.ServiceAccountKeyFile?.Length ?? 0);
}
