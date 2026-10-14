using System.Text.Json;
using Confluent.Kafka;
using GameServer.EventsWorker.Configuration;
using GameServer.EventsWorker.Models;
using GameServer.EventsWorker.Services;
using Microsoft.Extensions.Options;

namespace GameServer.EventsWorker;

/// <summary>
/// Reads game.events.v1 and writes every event into Postgres.
///
/// Offsets are committed manually, only after the row is persisted — at-least-once delivery.
/// A crash between the write and the commit replays the message, which the repository's
/// ON CONFLICT DO NOTHING makes harmless.
/// </summary>
public sealed class EventsConsumerWorker : BackgroundService
{
    private readonly KafkaConsumerOptions _options;
    private readonly IGameEventRepository _repository;
    private readonly ILogger<EventsConsumerWorker> _logger;

    public EventsConsumerWorker(
        IOptions<KafkaConsumerOptions> options,
        IGameEventRepository repository,
        ILogger<EventsConsumerWorker> logger)
    {
        _options = options.Value;
        _repository = repository;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Consume() blocks, so the loop gets its own thread instead of starving the host's.
        return Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
    }

    private void ConsumeLoop(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig)
            .SetErrorHandler((_, error) => _logger.LogWarning(
                "Kafka consumer error. Code: {Code}, Reason: {Reason}", error.Code, error.Reason))
            .SetPartitionsAssignedHandler((_, partitions) => _logger.LogInformation(
                "Partitions assigned: [{Partitions}]",
                string.Join(", ", partitions.Select(x => x.Partition.Value))))
            .SetPartitionsRevokedHandler((_, partitions) => _logger.LogInformation(
                "Partitions revoked: [{Partitions}]",
                string.Join(", ", partitions.Select(x => x.TopicPartition.Partition.Value))))
            .Build();

        using var deadLetterProducer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers
        }).Build();

        consumer.Subscribe(_options.Topic);
        _logger.LogInformation(
            "Subscribed to {Topic} as group {GroupId}.", _options.Topic, _options.GroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                if (result?.Message is null)
                {
                    continue;
                }

                GameEventEnvelope envelope;
                try
                {
                    envelope = Parse(result.Message.Value);
                }
                catch (Exception ex) when (ex is JsonException or FormatException)
                {
                    // Bad payloads must never block the partition: park them and move on.
                    _logger.LogWarning(ex,
                        "Unparseable event moved to the dead-letter topic. Partition: {Partition}, Offset: {Offset}",
                        result.Partition.Value, result.Offset.Value);
                    deadLetterProducer.Produce(_options.DeadLetterTopic, result.Message);
                    consumer.Commit(result);
                    continue;
                }

                try
                {
                    _repository
                        .InsertAsync(result.Partition.Value, result.Offset.Value, envelope, stoppingToken)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Storage problem, not a bad message. Rewind so the same offset is retried
                    // instead of silently skipped, then back off.
                    _logger.LogError(ex,
                        "Failed to store event, will retry. Partition: {Partition}, Offset: {Offset}",
                        result.Partition.Value, result.Offset.Value);
                    consumer.Seek(result.TopicPartitionOffset);
                    Thread.Sleep(TimeSpan.FromSeconds(2));
                    continue;
                }

                consumer.Commit(result);
                _logger.LogInformation(
                    "Stored event. Partition: {Partition}, Offset: {Offset}, Player: {PlayerId}, Event: {Event}",
                    result.Partition.Value, result.Offset.Value, envelope.PlayerId, envelope.Event);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Consumer loop cancelled.");
        }
        finally
        {
            // Leaves the group cleanly so the remaining instances rebalance immediately.
            consumer.Close();
            deadLetterProducer.Flush(TimeSpan.FromSeconds(5));
        }
    }

    private static GameEventEnvelope Parse(string value)
    {
        var envelope = JsonSerializer.Deserialize<GameEventEnvelope>(value)
                       ?? throw new FormatException("Event body deserialized to null.");

        if (string.IsNullOrWhiteSpace(envelope.PlayerId))
        {
            throw new FormatException("Event is missing playerId.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Event))
        {
            throw new FormatException("Event is missing event name.");
        }

        if (!envelope.OccurredAtUtc.HasValue)
        {
            throw new FormatException("Event is missing occurredAtUtc.");
        }

        return envelope;
    }
}
