using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RabWorker.Contracts;

namespace RabWorker.Worker;

public sealed class FormWorker(IConfiguration config, ILogger<FormWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var prefetch = config.GetValue("Worker:PrefetchCount", (ushort)32);
        var factory = new ConnectionFactory
        {
            Uri = new Uri(config["Worker:RabbitMqUri"]!),
            ConsumerDispatchConcurrency = config.GetValue("Worker:Concurrency", (ushort)4),
        };

        await using var dataSource = NpgsqlDataSource.Create(config["Worker:PostgresConnectionString"]!);
        var repo = new FormRepository(dataSource);
        await RetryAsync(() => repo.EnsureSchemaAsync(ct), ct);

        IConnection connection = null!;
        await RetryAsync(async () => connection = await factory.CreateConnectionAsync(ct), ct);
        await using var _ = connection;
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await RabbitTopology.DeclareAsync(channel, ct);
        await channel.BasicQosAsync(0, prefetch, false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            // Ack/nack must not use the stopping token: in-flight messages still need settling on shutdown.
            try
            {
                var msg = FormUpdateParser.Parse(ea.Body.Span);
                await RetryAsync(() => repo.ApplyAsync(msg, ct), ct);
                await channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dead-lettering message {MessageId}", ea.BasicProperties.MessageId);
                await channel.BasicRejectAsync(ea.DeliveryTag, requeue: false);
            }
        };
        await channel.BasicConsumeAsync(RabbitTopology.Queue, autoAck: false, consumer, ct);
        logger.LogInformation("Consuming {Queue}", RabbitTopology.Queue);

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Retries transient DB/broker failures with exponential backoff (max 30s) until success or shutdown.
    /// Done in-process rather than by requeueing so an outage doesn't burn the queue's delivery limit.
    /// </summary>
    private async Task RetryAsync(Func<Task> action, CancellationToken ct)
    {
        for (var delay = 1; ; delay = Math.Min(delay * 2, 30))
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when ((ex is NpgsqlException { IsTransient: true } or BrokerUnreachableException)
                                       && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Transient failure; retrying in {Delay}s", delay);
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
        }
    }
}
