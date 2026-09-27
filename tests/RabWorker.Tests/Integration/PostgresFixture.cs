using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RabWorker.Worker;
using Testcontainers.PostgreSql;

namespace RabWorker.Tests.Integration;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = NpgsqlDataSource.Create(ConnectionString);
        await new SchemaInitializer(DataSource, NullLogger<SchemaInitializer>.Instance).StartAsync(default);
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}
