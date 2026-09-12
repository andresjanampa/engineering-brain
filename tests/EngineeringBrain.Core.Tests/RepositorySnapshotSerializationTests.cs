using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class RepositorySnapshotSerializationTests
{
    [Fact]
    public void Snapshot_RoundTripsThroughJson()
    {
        var entity = new CodeEntity(
            "entity:abc",
            "Widget",
            "Demo.Widget",
            CodeEntityType.Class,
            "C#",
            "src/Widget.cs",
            3,
            7,
            "project-id",
            ResolutionLevel.Semantic,
            []);
        var snapshot = new RepositorySnapshot(
            2,
            new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero),
            new RepositoryInfo("repo-id", "demo", "/work/demo"),
            new GitInfo(true, "main", "abc123", null, true),
            [new ScannedFile("src/Widget.cs", ".cs", "C#", 42, "hash")],
            [new LanguageStatistics("C#", 1, 42)],
            [],
            [entity],
            [],
            new AnalysisSummary(AnalysisMode.FullSemantic, "test-v2", 1, 1, 0),
            []);

        var json = SnapshotJsonSerializer.Serialize(snapshot);
        var restored = SnapshotJsonSerializer.Deserialize(json);

        Assert.Equal(snapshot.Repository, restored.Repository);
        Assert.Equal(snapshot.Git, restored.Git);
        Assert.Equal(2, restored.SchemaVersion);
        var restoredEntity = Assert.Single(restored.Entities);
        Assert.Equal(entity.Id, restoredEntity.Id);
        Assert.Equal(entity.ProjectId, restoredEntity.ProjectId);
        Assert.Equal(entity.ResolutionLevel, restoredEntity.ResolutionLevel);
        Assert.Empty(restoredEntity.AdditionalLocations);
        Assert.Contains("\"entityType\": \"class\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_RejectsUnsupportedSchemaWithRegenerationGuidance()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            SnapshotJsonSerializer.Deserialize("{ \"schemaVersion\": 1 }"));

        Assert.Contains("Regenerate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
