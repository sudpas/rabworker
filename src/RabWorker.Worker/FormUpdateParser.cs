using System.Globalization;
using System.Text.Json;
using RabWorker.Contracts;

namespace RabWorker.Worker;

public sealed class InvalidMessageException(string message, Exception? inner = null) : Exception(message, inner);

public static class FormUpdateParser
{
    /// <summary>Deserializes and validates a message. Rejecting unknown fields here is what makes the repository's SQL interpolation safe.</summary>
    public static FormUpdateMessage Parse(ReadOnlySpan<byte> body)
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

        if (msg is null || msg.PersonId == Guid.Empty || msg.Version <= 0 || msg.OccurredAt == default
            || msg.Fields is not { Count: > 0 })
            throw new InvalidMessageException("personId, version, occurredAt and non-empty fields are required.");

        foreach (var (name, value) in msg.Fields)
        {
            if (!FormFields.All.Contains(name)) throw new InvalidMessageException($"Unknown field '{name}'.");
            if (name == FormFields.DateOfBirth && value is not null
                && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new InvalidMessageException($"Field '{name}' must be a yyyy-MM-dd date.");
        }

        return msg;
    }
}
