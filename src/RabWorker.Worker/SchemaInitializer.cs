using Npgsql;

namespace RabWorker.Worker;

/// <summary>Applies Sql/schema.sql on startup. Registered before the consumer so it completes first.</summary>
public sealed class SchemaInitializer(NpgsqlDataSource dataSource, ILogger<SchemaInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var stream = typeof(SchemaInitializer).Assembly.GetManifestResourceStream("schema.sql")
            ?? throw new InvalidOperationException("Embedded schema.sql not found.");
        var sql = await new StreamReader(stream).ReadToEndAsync(cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var cmd = dataSource.CreateCommand(sql);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                logger.LogInformation("Database schema is up to date");
                return;
            }
            catch (NpgsqlException ex) when (ex.IsTransient && attempt < 30)
            {
                logger.LogWarning(ex, "Postgres unavailable (attempt {Attempt}); retrying in 2s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
