using System.Runtime.InteropServices;
using System.Text.Json;
using RabbitMQ.Client;
using RabWorker.Contracts;
using RabWorker.TestClient;

// Usage: dotnet run --project src/RabWorker.TestClient -- [--people N] [--updates N] [--seed N]
//        [--shuffle] [--duplicate-rate 0..1] [--delay-ms N] [--rabbit amqp://...]
string Arg(string name, string fallback) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;

var peopleCount = int.Parse(Arg("--people", "10"));
var updates = int.Parse(Arg("--updates", "100"));
var duplicateRate = double.Parse(Arg("--duplicate-rate", "0"));
var delayMs = int.Parse(Arg("--delay-ms", "0"));
var rng = new Random(int.Parse(Arg("--seed", "42")));
var generator = new FormDataGenerator(rng);

var people = Enumerable.Range(0, peopleCount).Select(_ => generator.NewPersonId()).ToArray();
var messages = people.Select(generator.FullForm)
    .Concat(Enumerable.Range(0, updates).Select(_ => generator.Patch(people[rng.Next(people.Length)])))
    .ToList();

// Shuffling deliberately breaks per-person ordering to exercise the worker's per-field version checks.
if (args.Contains("--shuffle")) rng.Shuffle(CollectionsMarshal.AsSpan(messages));

var factory = new ConnectionFactory { Uri = new Uri(Arg("--rabbit", "amqp://guest:guest@localhost:5672/")) };
await using var connection = await factory.CreateConnectionAsync();
// With confirmation tracking, BasicPublishAsync completes only once the broker has confirmed the message.
await using var channel = await connection.CreateChannelAsync(
    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
await RabbitTopology.DeclareAsync(channel);

var published = 0;
foreach (var message in messages)
{
    var props = new BasicProperties { Persistent = true, MessageId = message.MessageId.ToString() };
    var body = JsonSerializer.SerializeToUtf8Bytes(message, FormJson.Options);
    for (var copies = rng.NextDouble() < duplicateRate ? 2 : 1; copies > 0; copies--, published++)
        await channel.BasicPublishAsync("", RabbitTopology.Queue, mandatory: true, props, body);
    if (delayMs > 0) await Task.Delay(delayMs);
}

Console.WriteLine($"Published {published} messages ({messages.Count} unique) for {people.Length} people to '{RabbitTopology.Queue}'.");
