using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabWorker.Contracts;
using RabWorker.Worker;
using Testcontainers.RabbitMq;

namespace RabWorker.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class EndToEndTests(PostgresFixture db) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management").Build();
    private IHost _host = null!;
    private IConnection _connection = null!;
    private IChannel _channel = null!;

    public async Task InitializeAsync()
    {
        await _rabbit.StartAsync();

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:RabbitMqUri"] = _rabbit.GetConnectionString(),
            ["Worker:PostgresConnectionString"] = db.ConnectionString,
        });
        builder.Services.AddFormWorker(builder.Configuration);
        _host = builder.Build();
        await _host.StartAsync();

        _connection = await new ConnectionFactory { Uri = new Uri(_rabbit.GetConnectionString()) }.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        await RabbitTopology.DeclareAsync(_channel);
    }

    public async Task DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
        await _rabbit.DisposeAsync();
    }

    private ValueTask PublishAsync(byte[] body) =>
        _channel.BasicPublishAsync("", RabbitTopology.Queue, mandatory: true, new BasicProperties { Persistent = true }, body);

    private static byte[] Message(Guid person, long version, object fields) =>
        JsonSerializer.SerializeToUtf8Bytes(new FormUpdateMessage(Guid.NewGuid(), person, version, DateTimeOffset.UtcNow,
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(fields))!), FormJson.Options);

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met within 20s.");
            await Task.Delay(200);
        }
    }

    [Fact]
    public async Task Worker_applies_published_updates_to_postgres()
    {
        var person = Guid.NewGuid();
        await PublishAsync(Message(person, 2, new { email = "new@x.com" }));
        await PublishAsync(Message(person, 1, new { first_name = "Alan", email = "old@x.com" }));

        await Eventually(async () =>
        {
            await using var cmd = db.DataSource.CreateCommand(
                "SELECT first_name = 'Alan' AND email = 'new@x.com' FROM person_form WHERE person_id = @id");
            cmd.Parameters.AddWithValue("id", person);
            return await cmd.ExecuteScalarAsync() is true;
        });
    }

    [Fact]
    public async Task Invalid_message_is_dead_lettered()
    {
        await PublishAsync(Encoding.UTF8.GetBytes("""{"personId":"not-a-guid"}"""));

        await Eventually(async () =>
            (await _channel.QueueDeclarePassiveAsync(RabbitTopology.DeadLetterQueue)).MessageCount > 0);
    }
}
