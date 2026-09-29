using Npgsql;
using RabWorker.Contracts;
using RabWorker.Worker;

namespace RabWorker.Tests.Integration;

[Trait("Category", "Integration")]
public class FormRepositoryTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly FormRepository _repo = new(db.DataSource);

    private static FormUpdateMessage Patch(Guid person, long version, params (string Field, string? Value)[] values) =>
        new(Guid.NewGuid(), person, version, DateTimeOffset.UtcNow, values.ToDictionary(v => v.Field, v => v.Value));

    private async Task<Dictionary<string, object?>> RowAsync(Guid person)
    {
        await using var cmd = db.DataSource.CreateCommand("SELECT * FROM person_form WHERE person_id = @id");
        cmd.Parameters.AddWithValue("id", person);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return Enumerable.Range(0, reader.FieldCount)
            .ToDictionary(reader.GetName, i => reader.IsDBNull(i) ? null : reader.GetValue(i));
    }

    [Fact]
    public async Task Out_of_order_patches_converge_per_field()
    {
        var person = Guid.NewGuid();

        // v3 arrives first, then v1 (full form), then v2. v1 must not overwrite v3's email.
        await _repo.ApplyAsync(Patch(person, 3, (FormFields.Email, "v3@x.com")), default);
        await _repo.ApplyAsync(Patch(person, 1,
            (FormFields.FirstName, "Ada"), (FormFields.Email, "v1@x.com"), (FormFields.Phone, "111")), default);
        await _repo.ApplyAsync(Patch(person, 2, (FormFields.Phone, "222")), default);

        var row = await RowAsync(person);
        Assert.Equal("Ada", row["first_name"]);
        Assert.Equal("v3@x.com", row["email"]);
        Assert.Equal("222", row["phone"]);
        Assert.Equal(3L, row["version"]);
    }

    [Fact]
    public async Task Replayed_message_is_idempotent()
    {
        var person = Guid.NewGuid();
        var patch = Patch(person, 1, (FormFields.FirstName, "Grace"), (FormFields.DateOfBirth, "1906-12-09"));

        await _repo.ApplyAsync(patch, default);
        await _repo.ApplyAsync(patch, default);
        Assert.Equal(new DateOnly(1906, 12, 9), (await RowAsync(person))["date_of_birth"]);
    }

    [Fact]
    public async Task Null_value_clears_field()
    {
        var person = Guid.NewGuid();
        await _repo.ApplyAsync(Patch(person, 1, (FormFields.Phone, "555")), default);
        await _repo.ApplyAsync(Patch(person, 2, (FormFields.Phone, null)), default);

        Assert.Null((await RowAsync(person))["phone"]);
    }
}
