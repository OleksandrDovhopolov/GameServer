using System.Text.Json;
using System.Text.Json.Nodes;
using Confluent.Kafka;
using GameServer.Api.Configuration;
using GameServer.Api.Models.Domain;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace GameServer.Api.Services;

/// <summary>
/// Publishes gameplay analytics events to Kafka. The player id is used as the message key so that
/// every event for one player lands in the same partition and keeps its order.
/// </summary>
public sealed class KafkaGameEventPublisher : IGameEventPublisher, IDisposable
{
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaGameEventPublisher> _logger;
    private readonly IProducer<string, string> _producer;

    public KafkaGameEventPublisher(IOptions<KafkaOptions> options, ILogger<KafkaGameEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.BootstrapServers))
        {
            throw new InvalidOperationException("Kafka:BootstrapServers is not configured.");
        }

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = _options.ProduceTimeoutSeconds * 1000
        };

        _producer = new ProducerBuilder<string, string>(producerConfig)
            .SetErrorHandler((_, error) => _logger.LogWarning(
                "Kafka producer error. Code: {Code}, Reason: {Reason}", error.Code, error.Reason))
            .Build();
    }

    public async Task<GameEventPublishResult> PublishAsync(
        string playerId,
        string eventName,
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("playerId is required.", nameof(playerId));
        }

        if (string.IsNullOrWhiteSpace(eventName))
        {
            throw new ArgumentException("eventName is required.", nameof(eventName));
        }

        var envelope = new JsonObject
        {
            ["event"] = eventName,
            ["playerId"] = playerId,
            ["occurredAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };

        if (payload.HasValue)
        {
            envelope["payload"] = JsonNode.Parse(payload.Value.GetRawText());
        }

        var message = new Message<string, string>
        {
            Key = playerId,
            Value = envelope.ToJsonString()
        };

        var delivery = await _producer.ProduceAsync(_options.Topic, message, cancellationToken);

        _logger.LogInformation(
            "Game event published. Topic: {Topic}, Partition: {Partition}, Offset: {Offset}, Event: {Event}",
            delivery.Topic,
            delivery.Partition.Value,
            delivery.Offset.Value,
            eventName);

        return new GameEventPublishResult(true, delivery.Partition.Value, delivery.Offset.Value);
    }

    public void Dispose()
    {
        // Flush before disposing so messages still sitting in the producer buffer are not lost.
        _producer.Flush(TimeSpan.FromSeconds(_options.ProduceTimeoutSeconds));
        _producer.Dispose();
    }
}
