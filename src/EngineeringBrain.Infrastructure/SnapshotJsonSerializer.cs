using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public static class SnapshotJsonSerializer
{
    public const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(RepositorySnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);

    public static RepositorySnapshot Deserialize(string json)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<RepositorySnapshot>(json, Options)
                ?? throw new InvalidDataException("Snapshot JSON did not contain a repository snapshot.");
            if (snapshot.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported snapshot schema version {snapshot.SchemaVersion}; expected {CurrentSchemaVersion}. Regenerate the snapshot.");
            }

            if (snapshot.Repository is null
                || snapshot.Git is null
                || snapshot.Files is null
                || snapshot.Languages is null
                || snapshot.Projects is null
                || snapshot.Entities is null
                || snapshot.Relations is null
                || snapshot.Analysis is null
                || snapshot.Diagnostics is null
                || snapshot.Incremental is null)
            {
                throw new InvalidDataException(
                    "Snapshot JSON is missing required schema fields. Regenerate the snapshot.");
            }

            return snapshot;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Snapshot JSON is invalid or incompatible. Regenerate the snapshot.", exception);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
