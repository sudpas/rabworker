namespace RabWorker.Worker;

public sealed class WorkerOptions
{
    public const string Section = "Worker";

    public string RabbitMqUri { get; set; } = "amqp://guest:guest@localhost:5672/";
    public string PostgresConnectionString { get; set; } =
        "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=forms";

    /// <summary>Unacked messages the broker may push to this worker at once.</summary>
    public ushort PrefetchCount { get; set; } = 32;

    /// <summary>Messages processed in parallel. Safe because upserts are per-field version guarded.</summary>
    public ushort Concurrency { get; set; } = 4;
}
