using RabbitMQ.Client;

namespace RabWorker.Contracts;

/// <summary>
/// Queue topology shared by publisher and worker. Both declare it idempotently on startup, so the
/// arguments here must never diverge between them (RabbitMQ rejects re-declaration with different args).
/// </summary>
public static class RabbitTopology
{
    public const string Queue = "form-updates";
    public const string DeadLetterExchange = "form-updates.dlx";
    public const string DeadLetterQueue = "form-updates.dlq";

    /// <summary>Redeliveries before a message is dead-lettered (guards against crash-looping poison messages).</summary>
    public const int DeliveryLimit = 10;

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, routingKey: "",
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = DeadLetterExchange,
                ["x-delivery-limit"] = DeliveryLimit,
            },
            cancellationToken: cancellationToken);
    }
}
