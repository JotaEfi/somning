using System.Text.Json;
using System.Text.Json.Serialization;
using Somning.Integration.Tests.Support;

namespace Somning.Integration.Tests.Engine;

public sealed record FixtureEntry(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("expected")] string? Expected,
    [property: JsonPropertyName("clean")] bool Clean,
    [property: JsonPropertyName("expect_outcome")] string ExpectOutcome,
    [property: JsonPropertyName("expect_reason")] string? ExpectReason,
    [property: JsonPropertyName("voice")] string? Voice,
    [property: JsonPropertyName("generator")] string Generator,
    [property: JsonPropertyName("note")] string Note);

public static class FixtureManifest
{
    public static IReadOnlyList<FixtureEntry> Load()
    {
        var fixtures = JsonSerializer.Deserialize<FixtureEntry[]>(
            File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "manifest.json")))
            ?? throw new InvalidDataException("Fixture manifest is empty.");
        if (fixtures.Length != 13 || fixtures.Any(x => string.IsNullOrWhiteSpace(x.File)
            || x.ExpectOutcome is not ("text" or "empty") || string.IsNullOrWhiteSpace(x.Generator)
            || string.IsNullOrWhiteSpace(x.Kind)))
            throw new InvalidDataException("Fixture manifest has missing required fields or unexpected fixture count.");
        if (fixtures.Count(x => x.ExpectOutcome == "empty") != 5)
            throw new InvalidDataException("Fixture manifest must contain all five expected-empty fixtures.");
        return fixtures;
    }
}
