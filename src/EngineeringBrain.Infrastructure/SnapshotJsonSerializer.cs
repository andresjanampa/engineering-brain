using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public static class SnapshotJsonSerializer
{
    public const int CurrentSchemaVersion = 2;

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
