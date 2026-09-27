using System.Text.Json;
using RabbitMQ.Client;
using RabWorker.Contracts;
using RabWorker.TestClient;

// Usage: dotnet run --project src/RabWorker.TestClient -- [--people N] [--updates N] [--seed N]
//        [--shuffle] [--duplicate-rate 0..1] [--delay-ms N] [--rabbit amqp://...]
var opts = ParseArgs(args);
var rng = new Random(opts.Seed);
var generator = new FormDataGenerator(rng);

var messages = new List<FormUpdateMessage>();
var people = Enumerable.Range(0, opts.People).Select(_ => generator.NewPersonId()).ToArray();
messages.AddRange(people.Select(generator.FullForm));
for (var i = 0; i < opts.Updates; i++)
    messages.Add(generator.Patch(people[rng.Next(people.Length)]));

// Shuffling deliberately breaks per-person ordering to exercise the worker's per-field version checks.
if (opts.Shuffle) rng.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(messages));

var factory = new ConnectionFactory { Uri = new Uri(opts.RabbitUri), ClientProvidedName = "rabworker-test-client" };
await using var connection = await factory.CreateConnectionAsync();
// With confirmation tracking, BasicPublishAsync completes only once the broker has confirmed the message.
await using var channel = await connection.CreateChannelAsync(
    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
await RabbitTopology.DeclareAsync(channel);

var published = 0;
foreach (var message in messages)
{
    var copies = rng.NextDouble() < opts.DuplicateRate ? 2 : 1;
    for (var c = 0; c < copies; c++)
    {
        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.MessageId.ToString(),
            Type = "form.update",
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message, FormJson.Options);
        await channel.BasicPublishAsync(exchange: "", routingKey: RabbitTopology.Queue, mandatory: true, props, body);
        published++;
    }
    if (opts.DelayMs > 0) await Task.Delay(opts.DelayMs);
}

Console.WriteLine($"Published {published} messages ({messages.Count} unique) for {people.Length} people to '{RabbitTopology.Queue}'.");

static Options ParseArgs(string[] args)
{
    var o = new Options();
    for (var i = 0; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
        switch (args[i])
        {
            case "--people": o = o with { People = int.Parse(Next()) }; break;
            case "--updates": o = o with { Updates = int.Parse(Next()) }; break;
            case "--seed": o = o with { Seed = int.Parse(Next()) }; break;
            case "--shuffle": o = o with { Shuffle = true }; break;
            case "--duplicate-rate": o = o with { DuplicateRate = double.Parse(Next()) }; break;
            case "--delay-ms": o = o with { DelayMs = int.Parse(Next()) }; break;
            case "--rabbit": o = o with { RabbitUri = Next() }; break;
            default: throw new ArgumentException($"Unknown argument {args[i]}");
        }
    }
    return o;
}

internal sealed record Options(
    int People = 10,
    int Updates = 100,
    int Seed = 42,
    bool Shuffle = false,
    double DuplicateRate = 0,
    int DelayMs = 0,
    string RabbitUri = "amqp://guest:guest@localhost:5672/");
