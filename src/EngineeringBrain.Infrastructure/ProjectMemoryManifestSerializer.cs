using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public static class ProjectMemoryManifestSerializer
{
    public const int CurrentKnowledgeSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ProjectMemoryManifest manifest) =>
        JsonSerializer.Serialize(manifest, Options).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    public static ProjectMemoryManifest Deserialize(string json)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<ProjectMemoryManifest>(json, Options)
                ?? throw new InvalidDataException("Knowledge manifest is empty.");
            if (manifest.KnowledgeSchemaVersion != CurrentKnowledgeSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Knowledge schema {manifest.KnowledgeSchemaVersion} is incompatible with schema {CurrentKnowledgeSchemaVersion}.");
            }

            if (string.IsNullOrWhiteSpace(manifest.RepositoryId)
                || string.IsNullOrWhiteSpace(manifest.Branch)
                || string.IsNullOrWhiteSpace(manifest.BranchKey)
                || manifest.Notes is null)
            {
                throw new InvalidDataException("Knowledge manifest is missing required fields.");
            }

            return manifest;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Knowledge manifest JSON is invalid.", exception);
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
