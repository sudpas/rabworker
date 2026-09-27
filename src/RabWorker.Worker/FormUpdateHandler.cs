using Npgsql;

namespace RabWorker.Worker;

public sealed class FormUpdateHandler(FormRepository repository, ILogger<FormUpdateHandler> logger)
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Parses and applies one message. Transient database errors are retried here indefinitely (until
    /// cancellation) rather than by requeueing, so a database outage doesn't burn the queue's delivery limit.
    /// </summary>
    /// <exception cref="InvalidMessageException">The message can never succeed and should be dead-lettered.</exception>
    public async Task HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var patch = FormUpdateParser.Parse(body.Span);

        var backoff = TimeSpan.FromMilliseconds(200);
        while (true)
        {
            try
            {
                var applied = await repository.ApplyAsync(patch, cancellationToken);
                logger.LogDebug("{Outcome} message {MessageId} for person {PersonId} v{Version} ({Fields})",
                    applied ? "Applied" : "Skipped stale", patch.MessageId, patch.PersonId, patch.Version,
                    string.Join(",", patch.Values.Keys));
                return;
            }
            catch (NpgsqlException ex) when (ex.IsTransient && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Transient database error for message {MessageId}; retrying in {Backoff}",
                    patch.MessageId, backoff);
                await Task.Delay(backoff, cancellationToken);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
        }
    }
}
