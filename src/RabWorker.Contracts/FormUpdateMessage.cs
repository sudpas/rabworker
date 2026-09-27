using System.Text.Json;

namespace RabWorker.Contracts;

/// <summary>
/// A partial update to one person's form. Only the keys present in <see cref="Fields"/> are changed;
/// a key with a JSON null value clears that field. <see cref="Version"/> is monotonically increasing
/// per person and is what the worker uses to discard stale or duplicate writes (per field).
/// </summary>
public sealed record FormUpdateMessage(
    Guid MessageId,
    Guid PersonId,
    long Version,
    DateTimeOffset OccurredAt,
    Dictionary<string, JsonElement> Fields);

public static class FormJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
