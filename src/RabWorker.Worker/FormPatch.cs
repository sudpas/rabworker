using System.Globalization;
using System.Text.Json;
using RabWorker.Contracts;

namespace RabWorker.Worker;

/// <summary>A validated <see cref="FormUpdateMessage"/> with field values converted to CLR types.</summary>
public sealed record FormPatch(
    Guid MessageId,
    Guid PersonId,
    long Version,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, object?> Values);

public sealed class InvalidMessageException(string message, Exception? inner = null) : Exception(message, inner);

public static class FormUpdateParser
{
    public static FormPatch Parse(ReadOnlySpan<byte> body)
    {
        FormUpdateMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<FormUpdateMessage>(body, FormJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidMessageException("Body is not a valid FormUpdateMessage.", ex);
        }

        if (msg is null) throw new InvalidMessageException("Body is null.");
        if (msg.PersonId == Guid.Empty) throw new InvalidMessageException("personId is required.");
        if (msg.Version <= 0) throw new InvalidMessageException("version must be positive.");
        if (msg.OccurredAt == default) throw new InvalidMessageException("occurredAt is required.");
        if (msg.Fields is null || msg.Fields.Count == 0) throw new InvalidMessageException("fields must not be empty.");

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, element) in msg.Fields)
        {
            if (!FormFields.All.Contains(name)) throw new InvalidMessageException($"Unknown field '{name}'.");
            values[name] = ConvertValue(name, element);
        }

        return new FormPatch(msg.MessageId, msg.PersonId, msg.Version, msg.OccurredAt, values);
    }

    private static object? ConvertValue(string name, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.String)
            throw new InvalidMessageException($"Field '{name}' must be a string or null.");

        var text = element.GetString()!;
        if (name != FormFields.DateOfBirth) return text;

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidMessageException($"Field '{name}' must be a yyyy-MM-dd date.");
    }
}
