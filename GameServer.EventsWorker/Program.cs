using GameServer.EventsWorker;
using GameServer.EventsWorker.Configuration;
using GameServer.EventsWorker.Services;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<KafkaConsumerOptions>(
    builder.Configuration.GetSection(KafkaConsumerOptions.SectionName));
builder.Services.Configure<PostgresOptions>(
    builder.Configuration.GetSection(PostgresOptions.SectionName));

builder.Services.AddSingleton(serviceProvider =>
{
    var postgresOptions = serviceProvider.GetRequiredService<IOptions<PostgresOptions>>().Value;
    if (string.IsNullOrWhiteSpace(postgresOptions.ConnectionString))
    {
        throw new InvalidOperationException("Postgres:ConnectionString is not configured.");
    }

    return NpgsqlDataSource.Create(postgresOptions.ConnectionString);
});

builder.Services.AddSingleton<IGameEventRepository, PostgresGameEventRepository>();
builder.Services.AddHostedService<EventsConsumerWorker>();

var host = builder.Build();
host.Run();
