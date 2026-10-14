using System.Text;
using System.Threading.RateLimiting;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using GameServer.Api.Configuration;
using GameServer.Api.Repositories;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Filters;
using GameServer.Api.Swagger;
using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
{
    options.Filters.Add<PlayerScopeFilter>();
});

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
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Bearer JWT header. Format: Bearer {token}"
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
builder.Services.Configure<SaveStorageOptions>(builder.Configuration.GetSection(SaveStorageOptions.SectionName));
builder.Services.Configure<AdminToolsOptions>(builder.Configuration.GetSection(AdminToolsOptions.SectionName));
builder.Services.Configure<GooglePlayBillingOptions>(builder.Configuration.GetSection(GooglePlayBillingOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.SectionName));

var googlePlayBillingOptions = builder.Configuration.GetSection(GooglePlayBillingOptions.SectionName).Get<GooglePlayBillingOptions>() ?? new GooglePlayBillingOptions();
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
var adminAuthOptions = builder.Configuration.GetSection("AdminAuth").Get<AdminAuthOptions>() ?? new AdminAuthOptions();
LogGooglePlayBillingConfigurationState(builder.Environment, googlePlayBillingOptions);
ValidateGooglePlayBillingConfiguration(builder.Environment, googlePlayBillingOptions);
ValidateAuthConfiguration(authOptions);
ValidateAdminAuthConfiguration(builder.Environment, adminAuthOptions);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GetJwtValidationKey(authOptions))),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-anonymous", context => RateLimitPartition.GetFixedWindowLimiter(
        GetClientPartitionKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, authOptions.AnonymousLimitPerMinute),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("auth-refresh", context => RateLimitPartition.GetFixedWindowLimiter(
        GetClientPartitionKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, authOptions.RefreshLimitPerMinute),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services.AddSingleton(sp =>
{
    var postgresOptions = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
    if (string.IsNullOrWhiteSpace(postgresOptions.ConnectionString))
        throw new InvalidOperationException("Postgres:ConnectionString is not configured.");
    return Npgsql.NpgsqlDataSource.Create(postgresOptions.ConnectionString);
});
builder.Services.AddScoped<IConfigRepository, PostgresConfigRepository>();
builder.Services.AddScoped<IConfigService, ConfigService>();
builder.Services.AddScoped<IPlayerAccountRepository, PostgresPlayerAccountRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, PostgresRefreshTokenRepository>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

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
builder.Services.AddScoped<RedisPlayerAggregateRepository>();
builder.Services.AddScoped<RedisPlayerResourcesRepository>();
builder.Services.AddScoped<RedisPlayerInventoryRepository>();
builder.Services.AddScoped<PostgresSaveRepository>();
builder.Services.AddScoped<PostgresPlayerAggregateRepository>();
builder.Services.AddScoped<PostgresPlayerResourcesRepository>();
builder.Services.AddScoped<PostgresPlayerInventoryRepository>();
builder.Services.AddScoped<ISaveRepository>(ResolveSaveRepository);
builder.Services.AddScoped<IPlayerAggregateRepository>(ResolvePlayerAggregateRepository);
builder.Services.AddScoped<IPlayerResourcesRepository>(ResolvePlayerResourcesRepository);
builder.Services.AddScoped<IPlayerInventoryRepository>(ResolvePlayerInventoryRepository);
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
builder.Services.AddScoped<ISaveStorageBackfillService, SaveStorageBackfillService>();
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
builder.Services.AddScoped<PlayerScopeFilter>();
builder.Services.AddSingleton<IRandomGenerator, RandomGenerator>();
builder.Services.AddSingleton<ISystemClock, SystemClock>();
builder.Services.AddSingleton<IWheelRewardCatalog, WheelRewardCatalog>();
builder.Services.AddScoped<IWheelService, WheelService>();

var kafkaOptions = builder.Configuration.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>() ?? new KafkaOptions();
if (kafkaOptions.Enabled)
{
    builder.Services.AddSingleton<IGameEventPublisher, KafkaGameEventPublisher>();
}
else
{
    builder.Services.AddSingleton<IGameEventPublisher, NullGameEventPublisher>();
}

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

app.Logger.LogInformation(
    "Save storage provider startup state. Provider: {Provider}",
    app.Services.GetRequiredService<IOptions<SaveStorageOptions>>().Value.Provider);
app.Logger.LogInformation(
    "Auth startup state. Mode: {Mode}, Issuer: {Issuer}, KeyId: {KeyId}, JwtSigningKeySet: {JwtSigningKeySet}",
    authOptions.Mode,
    authOptions.Issuer,
    authOptions.KeyId,
    !string.IsNullOrWhiteSpace(authOptions.JwtSigningKey));

// Force reward catalog initialization at startup so invalid configuration fails fast.
app.Services.GetRequiredService<IWheelRewardCatalog>();
app.Services.GetRequiredService<IRewardDefinitionRepository>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpLogging();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/ping", () => "pong");

app.Run();

static void ValidateGooglePlayBillingConfiguration(IWebHostEnvironment environment, GooglePlayBillingOptions options)
{
    if (!options.Enabled)
    {
        if (!environment.IsDevelopment() && !environment.IsStaging())
        {
            throw new InvalidOperationException("GooglePlayBilling must be enabled outside Development/Staging.");
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

static void ValidateAuthConfiguration(AuthOptions options)
{
    if (options.Mode == AuthMode.Disabled)
    {
        return;
    }

    if (string.IsNullOrWhiteSpace(options.JwtSigningKey))
    {
        throw new InvalidOperationException("Auth:JwtSigningKey is not configured.");
    }

    if (Encoding.UTF8.GetByteCount(options.JwtSigningKey) < 32)
    {
        throw new InvalidOperationException("Auth:JwtSigningKey must be at least 32 bytes for HS256.");
    }
}

static void ValidateAdminAuthConfiguration(IWebHostEnvironment environment, AdminAuthOptions options)
{
    if (environment.IsDevelopment())
    {
        return;
    }

    var username = Environment.GetEnvironmentVariable("ADMIN_USER") ?? options.Username;
    var password = Environment.GetEnvironmentVariable("ADMIN_PASS") ?? options.Password;

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException("Admin auth credentials must be configured outside Development.");
    }

    if (string.Equals(username, "admin", StringComparison.Ordinal) &&
        string.Equals(password, "admin", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Default admin credentials are not allowed outside Development.");
    }
}

static string GetJwtValidationKey(AuthOptions options)
{
    return string.IsNullOrWhiteSpace(options.JwtSigningKey)
        ? "GameServer.DisabledAuth.Placeholder.OnlyForStartup.32Bytes"
        : options.JwtSigningKey;
}

static string GetClientPartitionKey(HttpContext context)
{
    if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
    {
        var first = forwardedFor.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first;
        }
    }

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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

static ISaveRepository ResolveSaveRepository(IServiceProvider serviceProvider)
{
    var provider = serviceProvider.GetRequiredService<IOptions<SaveStorageOptions>>().Value.Provider;
    var redis = serviceProvider.GetRequiredService<RedisSaveRepository>();
    var postgres = serviceProvider.GetRequiredService<PostgresSaveRepository>();
    return provider switch
    {
        SaveStorageProvider.Redis => redis,
        SaveStorageProvider.Postgres => postgres,
        SaveStorageProvider.DualWriteReadRedis => new DualWriteSaveRepository(
            redis,
            postgres,
            serviceProvider.GetRequiredService<ILogger<DualWriteSaveRepository>>()),
        SaveStorageProvider.DualWriteReadPostgres => new DualWriteSaveRepository(
            postgres,
            redis,
            serviceProvider.GetRequiredService<ILogger<DualWriteSaveRepository>>()),
        _ => throw new InvalidOperationException($"Unsupported save storage provider '{provider}'.")
    };
}

static IPlayerAggregateRepository ResolvePlayerAggregateRepository(IServiceProvider serviceProvider)
{
    var provider = serviceProvider.GetRequiredService<IOptions<SaveStorageOptions>>().Value.Provider;
    var redis = serviceProvider.GetRequiredService<RedisPlayerAggregateRepository>();
    var postgres = serviceProvider.GetRequiredService<PostgresPlayerAggregateRepository>();
    return provider switch
    {
        SaveStorageProvider.Redis => redis,
        SaveStorageProvider.Postgres => postgres,
        SaveStorageProvider.DualWriteReadRedis => new DualWritePlayerAggregateRepository(
            redis,
            postgres,
            serviceProvider.GetRequiredService<ILogger<DualWritePlayerAggregateRepository>>()),
        SaveStorageProvider.DualWriteReadPostgres => new DualWritePlayerAggregateRepository(
            postgres,
            redis,
            serviceProvider.GetRequiredService<ILogger<DualWritePlayerAggregateRepository>>()),
        _ => throw new InvalidOperationException($"Unsupported save storage provider '{provider}'.")
    };
}

static IPlayerResourcesRepository ResolvePlayerResourcesRepository(IServiceProvider serviceProvider)
{
    var provider = serviceProvider.GetRequiredService<IOptions<SaveStorageOptions>>().Value.Provider;
    var redis = serviceProvider.GetRequiredService<RedisPlayerResourcesRepository>();
    var postgres = serviceProvider.GetRequiredService<PostgresPlayerResourcesRepository>();
    return provider switch
    {
        SaveStorageProvider.Redis => redis,
        SaveStorageProvider.Postgres => postgres,
        SaveStorageProvider.DualWriteReadRedis => new DualWritePlayerResourcesRepository(
            redis,
            postgres,
            serviceProvider.GetRequiredService<ILogger<DualWritePlayerResourcesRepository>>()),
        SaveStorageProvider.DualWriteReadPostgres => new DualWritePlayerResourcesRepository(
            postgres,
            redis,
            serviceProvider.GetRequiredService<ILogger<DualWritePlayerResourcesRepository>>()),
        _ => throw new InvalidOperationException($"Unsupported save storage provider '{provider}'.")
    };
}

static IPlayerInventoryRepository ResolvePlayerInventoryRepository(IServiceProvider serviceProvider)
{
    var provider = serviceProvider.GetRequiredService<IOptions<SaveStorageOptions>>().Value.Provider;
    var redis = serviceProvider.GetRequiredService<RedisPlayerInventoryRepository>();
    var postgres = serviceProvider.GetRequiredService<PostgresPlayerInventoryRepository>();
    return provider switch
    {
        SaveStorageProvider.Redis => redis,
        SaveStorageProvider.Postgres => postgres,
        SaveStorageProvider.DualWriteReadRedis => new DualWritePlayerInventoryRepository(
            redis,
            postgres,
            serviceProvider.GetRequiredService<ILogger<DualWritePlayerInventoryRepository>>()),
        SaveStorageProvider.DualWriteReadPostgres => new DualWritePlayerInventoryRepository(
            postgres,
            redis,
            serviceProvider.GetRequiredService<ILogger<DualWritePlayerInventoryRepository>>()),
        _ => throw new InvalidOperationException($"Unsupported save storage provider '{provider}'.")
    };
}
