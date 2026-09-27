using System.Text;
using Npgsql;
using NpgsqlTypes;
using RabWorker.Contracts;

namespace RabWorker.Worker;

public sealed class FormRepository(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Upserts the patch. Each field is written only if the patch version is newer than the version that
    /// last wrote that field, so replays and out-of-order delivery converge to the same state.
    /// </summary>
    /// <returns>False if every field in the patch was already at a newer or equal version (no-op).</returns>
    public async Task<bool> ApplyAsync(FormPatch patch, CancellationToken cancellationToken)
    {
        var fields = patch.Values.Keys.Order(StringComparer.Ordinal).ToArray();

        await using var cmd = dataSource.CreateCommand(BuildUpsertSql(fields));
        cmd.Parameters.Add(new NpgsqlParameter("person_id", NpgsqlDbType.Uuid) { Value = patch.PersonId });
        cmd.Parameters.Add(new NpgsqlParameter("version", NpgsqlDbType.Bigint) { Value = patch.Version });
        cmd.Parameters.Add(new NpgsqlParameter("occurred_at", NpgsqlDbType.TimestampTz)
            { Value = patch.OccurredAt.ToUniversalTime() });
        for (var i = 0; i < fields.Length; i++)
        {
            var dbType = fields[i] == FormFields.DateOfBirth ? NpgsqlDbType.Date : NpgsqlDbType.Text;
            cmd.Parameters.Add(new NpgsqlParameter($"p{i}", dbType) { Value = patch.Values[fields[i]] ?? DBNull.Value });
        }

        return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
    }

    // Field names come from the FormFields whitelist (enforced by FormUpdateParser), so interpolation is safe.
    internal static string BuildUpsertSql(IReadOnlyList<string> fields)
    {
        static string FieldVersion(string f) => $"COALESCE((t.field_versions->>'{f}')::bigint, 0)";

        var sb = new StringBuilder();
        sb.Append("INSERT INTO person_form AS t (person_id, ")
          .AppendJoin(", ", fields)
          .Append(", version, field_versions, updated_at)\nVALUES (@person_id, ")
          .AppendJoin(", ", fields.Select((_, i) => $"@p{i}"))
          .Append(", @version, jsonb_build_object(")
          .AppendJoin(", ", fields.Select(f => $"'{f}', @version"))
          .Append("), @occurred_at)\nON CONFLICT (person_id) DO UPDATE SET\n");

        foreach (var f in fields)
            sb.Append($"  {f} = CASE WHEN {FieldVersion(f)} < EXCLUDED.version THEN EXCLUDED.{f} ELSE t.{f} END,\n");

        sb.Append("  field_versions = t.field_versions || jsonb_build_object(")
          .AppendJoin(", ", fields.Select(f => $"'{f}', GREATEST({FieldVersion(f)}, EXCLUDED.version)"))
          .Append("),\n")
          .Append("  version = GREATEST(t.version, EXCLUDED.version),\n")
          .Append("  updated_at = CASE WHEN EXCLUDED.version > t.version THEN EXCLUDED.updated_at ELSE t.updated_at END\n")
          .Append("WHERE ")
          .AppendJoin(" OR ", fields.Select(f => $"{FieldVersion(f)} < EXCLUDED.version"))
          .Append("\nRETURNING version");

        return sb.ToString();
    }
}
