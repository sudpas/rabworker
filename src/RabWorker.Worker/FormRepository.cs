using Npgsql;
using NpgsqlTypes;
using RabWorker.Contracts;

namespace RabWorker.Worker;

public sealed class FormRepository(NpgsqlDataSource dataSource)
{
    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using var stream = typeof(FormRepository).Assembly.GetManifestResourceStream("schema.sql")!;
        await using var cmd = dataSource.CreateCommand(await new StreamReader(stream).ReadToEndAsync(ct));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Upserts the patch. Each field is written only if the message version is newer than the version that
    /// last wrote that field, so replays and out-of-order delivery converge. Column names are interpolated,
    /// which is safe only because <see cref="FormUpdateParser"/> restricts them to <see cref="FormFields.All"/>.
    /// </summary>
    public async Task ApplyAsync(FormUpdateMessage m, CancellationToken ct)
    {
        var f = m.Fields.Keys.ToArray();
        string Ver(string c) => $"COALESCE((t.field_versions->>'{c}')::bigint, 0)";
        string Join(Func<string, int, string> select) => string.Join(", ", f.Select(select));

        var sql = $"""
            INSERT INTO person_form AS t (person_id, {string.Join(", ", f)}, version, field_versions, updated_at)
            VALUES (@person_id, {Join((c, i) => c == FormFields.DateOfBirth ? $"@p{i}::date" : $"@p{i}")}, @version,
                    jsonb_build_object({Join((c, _) => $"'{c}', @version")}), @occurred_at)
            ON CONFLICT (person_id) DO UPDATE SET
              {Join((c, _) => $"{c} = CASE WHEN {Ver(c)} < EXCLUDED.version THEN EXCLUDED.{c} ELSE t.{c} END")},
              field_versions = t.field_versions || jsonb_build_object({Join((c, _) => $"'{c}', GREATEST({Ver(c)}, EXCLUDED.version)")}),
              version = GREATEST(t.version, EXCLUDED.version),
              updated_at = CASE WHEN EXCLUDED.version > t.version THEN EXCLUDED.updated_at ELSE t.updated_at END
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("person_id", m.PersonId);
        cmd.Parameters.AddWithValue("version", m.Version);
        cmd.Parameters.AddWithValue("occurred_at", m.OccurredAt.UtcDateTime);
        for (var i = 0; i < f.Length; i++)
            cmd.Parameters.Add(new NpgsqlParameter($"p{i}", NpgsqlDbType.Text)
                { Value = (object?)m.Fields[f[i]] ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
