using System.Text;
using RabWorker.Contracts;
using RabWorker.Worker;

namespace RabWorker.Tests;

public class FormUpdateParserTests
{
    private const string PersonId = "5b1f7c0e-8f4e-4a5a-9f59-0c2f7d6b3a11";

    private static FormUpdateMessage Parse(string json) => FormUpdateParser.Parse(Encoding.UTF8.GetBytes(json));

    private static string Msg(string fields, long version = 3) =>
        $$"""{"messageId":"{{Guid.NewGuid()}}","personId":"{{PersonId}}","version":{{version}},"occurredAt":"2026-01-02T03:04:05Z","fields":{{fields}}}""";

    [Fact]
    public void Parses_valid_patch_and_converts_types()
    {
        var patch = Parse(Msg("""{"email":"a@b.com","date_of_birth":"1990-05-17","phone":null}"""));

        Assert.Equal(Guid.Parse(PersonId), patch.PersonId);
        Assert.Equal(3, patch.Version);
        Assert.Equal("a@b.com", patch.Fields[FormFields.Email]);
        Assert.Equal("1990-05-17", patch.Fields[FormFields.DateOfBirth]);
        Assert.True(patch.Fields.ContainsKey(FormFields.Phone));
        Assert.Null(patch.Fields[FormFields.Phone]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    public void Rejects_malformed_body(string body) =>
        Assert.Throws<InvalidMessageException>(() => Parse(body));

    [Theory]
    [InlineData("""{"favorite_color":"red"}""")] // unknown field
    [InlineData("""{"date_of_birth":"17/05/1990"}""")] // bad date
    [InlineData("""{"email":42}""")] // wrong type
    [InlineData("{}")] // nothing to update
    public void Rejects_invalid_fields(string fields) =>
        Assert.Throws<InvalidMessageException>(() => Parse(Msg(fields)));

    [Fact]
    public void Rejects_non_positive_version() =>
        Assert.Throws<InvalidMessageException>(() => Parse(Msg("""{"email":"a@b.com"}""", version: 0)));
}
