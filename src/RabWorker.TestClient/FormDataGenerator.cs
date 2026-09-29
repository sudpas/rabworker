using RabWorker.Contracts;

namespace RabWorker.TestClient;

/// <summary>Generates form updates, tracking the per-person version counter (owned by the source system in real life).</summary>
public sealed class FormDataGenerator(Random rng)
{
    private static readonly string[] Names = ["Ava", "Liam", "Noah", "Emma", "Mia", "Arjun", "Priya", "Chen", "Sofia", "Omar"];
    private static readonly string[] Streets = ["Main St", "Oak Ave", "Pine Rd", "Maple Dr", "Cedar Ln", "Elm St"];
    private static readonly string[] Cities = ["Seattle", "Austin", "Denver", "Boston", "Chicago", "Portland"];
    private static readonly string[] States = ["WA", "TX", "CO", "MA", "IL", "OR"];

    private readonly Dictionary<Guid, long> _versions = new();

    public Guid NewPersonId()
    {
        Span<byte> bytes = stackalloc byte[16];
        rng.NextBytes(bytes);
        return new Guid(bytes);
    }

    /// <summary>Every field — each person's first submission.</summary>
    public FormUpdateMessage FullForm(Guid personId) =>
        Message(personId, FormFields.All.ToDictionary(f => f, string? (f) => RandomValue(f)));

    /// <summary>1–3 random fields, occasionally clearing an optional one.</summary>
    public FormUpdateMessage Patch(Guid personId)
    {
        var fields = new Dictionary<string, string?>();
        var count = rng.Next(1, 4);
        while (fields.Count < count)
        {
            var name = Pick(FormFields.All);
            var optional = name != FormFields.FirstName && name != FormFields.LastName;
            fields[name] = optional && rng.Next(15) == 0 ? null : RandomValue(name);
        }
        return Message(personId, fields);
    }

    private FormUpdateMessage Message(Guid personId, Dictionary<string, string?> fields) =>
        new(Guid.NewGuid(), personId, _versions[personId] = _versions.GetValueOrDefault(personId) + 1,
            DateTimeOffset.UtcNow, fields);

    private string RandomValue(string field) => field switch
    {
        FormFields.FirstName or FormFields.LastName => Pick(Names),
        FormFields.Email => $"{Pick(Names)}{rng.Next(1000)}@example.com".ToLowerInvariant(),
        FormFields.Phone => $"+1-555-{rng.Next(100, 1000)}-{rng.Next(1000, 10000)}",
        FormFields.DateOfBirth => new DateOnly(1950, 1, 1).AddDays(rng.Next(365 * 55)).ToString("yyyy-MM-dd"),
        FormFields.AddressLine1 => $"{rng.Next(1, 9999)} {Pick(Streets)}",
        FormFields.City => Pick(Cities),
        FormFields.State => Pick(States),
        _ => rng.Next(10000, 99999).ToString(), // postal_code
    };

    private T Pick<T>(T[] items) => items[rng.Next(items.Length)];
}
