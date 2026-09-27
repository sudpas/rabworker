using System.Text.Json;
using RabWorker.Contracts;

namespace RabWorker.TestClient;

/// <summary>
/// Generates a stream of form updates for a fixed set of people. Tracks the per-person version counter,
/// which in a real system would be owned by the source of truth that emits the updates.
/// </summary>
public sealed class FormDataGenerator(Random rng)
{
    private static readonly string[] FirstNames = ["Ava", "Liam", "Noah", "Emma", "Mia", "Arjun", "Priya", "Chen", "Sofia", "Omar"];
    private static readonly string[] LastNames = ["Smith", "Patel", "Garcia", "Kim", "Nguyen", "Brown", "Singh", "Lopez", "Khan", "Müller"];
    private static readonly string[] Streets = ["Main St", "Oak Ave", "Pine Rd", "Maple Dr", "Cedar Ln", "Elm St"];
    private static readonly (string City, string State)[] Places =
        [("Seattle", "WA"), ("Austin", "TX"), ("Denver", "CO"), ("Boston", "MA"), ("Chicago", "IL"), ("Portland", "OR")];

    private static readonly string[] OptionalFields =
    [
        FormFields.Email, FormFields.Phone, FormFields.DateOfBirth,
        FormFields.AddressLine1, FormFields.City, FormFields.State, FormFields.PostalCode,
    ];

    private readonly Dictionary<Guid, long> _versions = new();

    public Guid NewPersonId()
    {
        Span<byte> bytes = stackalloc byte[16];
        rng.NextBytes(bytes);
        var id = new Guid(bytes);
        _versions[id] = 0;
        return id;
    }

    /// <summary>A message containing every field — used as each person's first submission.</summary>
    public FormUpdateMessage FullForm(Guid personId)
    {
        var place = Pick(Places);
        var first = Pick(FirstNames);
        var last = Pick(LastNames);
        return Message(personId, new Dictionary<string, object?>
        {
            [FormFields.FirstName] = first,
            [FormFields.LastName] = last,
            [FormFields.Email] = Email(first, last),
            [FormFields.Phone] = Phone(),
            [FormFields.DateOfBirth] = DateOfBirth(),
            [FormFields.AddressLine1] = Address(),
            [FormFields.City] = place.City,
            [FormFields.State] = place.State,
            [FormFields.PostalCode] = PostalCode(),
        });
    }

    /// <summary>A message changing 1–3 random fields (occasionally clearing an optional one).</summary>
    public FormUpdateMessage Patch(Guid personId)
    {
        var fields = new Dictionary<string, object?>();
        var count = rng.Next(1, 4);
        while (fields.Count < count)
        {
            var name = rng.Next(10) == 0 ? Pick([FormFields.FirstName, FormFields.LastName]) : Pick(OptionalFields);
            fields[name] = OptionalFields.Contains(name) && rng.Next(15) == 0 ? null : RandomValue(name);
        }
        return Message(personId, fields);
    }

    private FormUpdateMessage Message(Guid personId, Dictionary<string, object?> fields) => new(
        Guid.NewGuid(),
        personId,
        ++_versions[personId],
        DateTimeOffset.UtcNow,
        fields.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value, FormJson.Options)));

    private string RandomValue(string field) => field switch
    {
        FormFields.FirstName => Pick(FirstNames),
        FormFields.LastName => Pick(LastNames),
        FormFields.Email => Email(Pick(FirstNames), Pick(LastNames)),
        FormFields.Phone => Phone(),
        FormFields.DateOfBirth => DateOfBirth(),
        FormFields.AddressLine1 => Address(),
        FormFields.City => Pick(Places).City,
        FormFields.State => Pick(Places).State,
        FormFields.PostalCode => PostalCode(),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    private T Pick<T>(T[] items) => items[rng.Next(items.Length)];
    private string Email(string first, string last) => $"{first}.{last}{rng.Next(1000)}@example.com".ToLowerInvariant();
    private string Phone() => $"+1-555-{rng.Next(100, 1000)}-{rng.Next(1000, 10000)}";
    private string DateOfBirth() => new DateOnly(1950, 1, 1).AddDays(rng.Next(365 * 55)).ToString("yyyy-MM-dd");
    private string Address() => $"{rng.Next(1, 9999)} {Pick(Streets)}";
    private string PostalCode() => rng.Next(10000, 99999).ToString();
}
