using System.Text.Json;

namespace RabWorker.Contracts;

/// <summary>
/// A partial update to one person's form. Only keys present in <see cref="Fields"/> change; a null value
/// clears the field. <see cref="Version"/> increases per person and orders writes (per field).
/// </summary>
public sealed record FormUpdateMessage(
    Guid MessageId,
    Guid PersonId,
    long Version,
    DateTimeOffset OccurredAt,
    Dictionary<string, string?> Fields);

public static class FormJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
