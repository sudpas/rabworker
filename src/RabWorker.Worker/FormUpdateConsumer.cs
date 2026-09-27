using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RabWorker.Contracts;

namespace RabWorker.Worker;

public sealed class FormUpdateConsumer(
    IOptions<WorkerOptions> options,
    FormUpdateHandler handler,
    ILogger<FormUpdateConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var factory = new ConnectionFactory
        {
            Uri = new Uri(opts.RabbitMqUri),
            ConsumerDispatchConcurrency = opts.Concurrency,
            ClientProvidedName = "rabworker-worker",
        };

        await using var connection = await ConnectWithRetryAsync(factory, stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: opts.PrefetchCount, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => OnReceivedAsync(channel, ea, stoppingToken);
        await channel.BasicConsumeAsync(RabbitTopology.Queue, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Consuming {Queue} (prefetch {Prefetch}, concurrency {Concurrency})",
            RabbitTopology.Queue, opts.PrefetchCount, opts.Concurrency);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        // Ack/nack must not use stoppingToken: in-flight messages still need settling during shutdown.
        try
        {
            await handler.HandleAsync(ea.Body, stoppingToken);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (InvalidMessageException ex)
        {
            logger.LogWarning(ex, "Dead-lettering invalid message {MessageId}", ea.BasicProperties.MessageId);
            await channel.BasicRejectAsync(ea.DeliveryTag, requeue: false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dead-lettering message {MessageId} after non-transient failure",
                ea.BasicProperties.MessageId);
            await channel.BasicRejectAsync(ea.DeliveryTag, requeue: false);
        }
    }

    private async Task<IConnection> ConnectWithRetryAsync(ConnectionFactory factory, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                return await factory.CreateConnectionAsync(ct);
            }
            catch (BrokerUnreachableException ex)
            {
                logger.LogWarning(ex, "RabbitMQ unreachable; retrying in 2s");
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }
}
