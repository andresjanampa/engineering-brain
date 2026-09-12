using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ProjectMemoryBuilderTests
{
    [Fact]
    public void Build_CreatesManifestIndexesProjectsAndTopLevelComponentsWithProvenance()
    {
        var snapshot = ProjectMemoryTestFactory.Create();

        var build = new ProjectMemoryBuilder().Build(snapshot);

        Assert.Equal(1, build.Manifest.KnowledgeSchemaVersion);
        Assert.Equal(snapshot.Repository.Id, build.Manifest.RepositoryId);
        Assert.Equal(snapshot.Git.Branch, build.Manifest.Branch);
        Assert.Contains(build.Notes, note => note.ManifestEntry.RelativePath == "index.md");
        Assert.Contains(build.Notes, note => note.ManifestEntry.RelativePath == "architecture/overview.md");
        Assert.Equal(2, build.Manifest.Notes.Count(note => note.Kind == KnowledgeNoteKind.Project));
        Assert.Equal(4, build.Manifest.Notes.Count(note => note.Kind == KnowledgeNoteKind.Component));
        Assert.DoesNotContain(build.Manifest.Notes, note => note.SourceId == "entity:nested");
        Assert.All(build.Notes, note =>
        {
            Assert.Contains("knowledge_schema: 1", note.Content, StringComparison.Ordinal);
            Assert.Contains("source_fingerprint:", note.Content, StringComparison.Ordinal);
            Assert.Contains("managed_by: engineering-brain", note.Content, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Build_GeneratesCompactFactsWithoutSourceBodiesOrAbsolutePaths()
    {
        var build = new ProjectMemoryBuilder().Build(ProjectMemoryTestFactory.Create());
        var service = Assert.Single(build.Notes, note => note.ManifestEntry.SourceId == "entity:service");

        Assert.Contains("Demo.Core.Service.Run(int)", service.Content, StringComparison.Ordinal);
        Assert.Contains("Property: `Demo.Core.Service.Name`", service.Content, StringComparison.Ordinal);
        Assert.Contains("Class: `Demo.Core.Service.Nested`", service.Content, StringComparison.Ordinal);
        Assert.Contains("src/Core/Services.cs", service.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\private", service.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("public class", service.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("=>", service.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DuplicateNamesAcrossNamespacesAndProjectsCannotCollide()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var core = snapshot.Projects[0];
        var business = snapshot.Projects[1];
        var first = ProjectMemoryTestFactory.Entity(
            "entity:duplicate-one",
            "Customer",
            "NamespaceA.Customer",
            CodeEntityType.Class,
            "src/Core/Customer.cs",
            core.Id);
        var second = ProjectMemoryTestFactory.Entity(
            "entity:duplicate-two",
            "Customer",
            "NamespaceB.Customer",
            CodeEntityType.Class,
            "src/Business/Customer.cs",
            business.Id);
        snapshot = snapshot with { Entities = snapshot.Entities.Concat([first, second]).ToArray() };

        var notes = new ProjectMemoryBuilder().Build(snapshot).Manifest.Notes
            .Where(note => note.SourceId is "entity:duplicate-one" or "entity:duplicate-two")
            .ToArray();

        Assert.Equal(2, notes.Length);
        Assert.Equal(2, notes.Select(note => note.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(notes, note => Assert.StartsWith("components/Customer--", note.RelativePath, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_PartialEntityProducesOneComponentNoteWithAllLocations()
    {
        var build = new ProjectMemoryBuilder().Build(ProjectMemoryTestFactory.Create());

        var notes = build.Notes.Where(note => note.ManifestEntry.SourceId == "entity:service").ToArray();

        var note = Assert.Single(notes);
        Assert.Contains("src/Core/Services.cs", note.Content, StringComparison.Ordinal);
        Assert.Contains("src/Core/Service.Part2.cs", note.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_BoundsLargeMemberListsAndReportsRemainder()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var service = snapshot.Entities.Single(entity => entity.Id == "entity:service");
        var members = Enumerable.Range(0, 55)
            .Select(index => ProjectMemoryTestFactory.Entity(
                $"entity:member-{index:D2}",
                $"Method{index:D2}",
                $"Demo.Core.Service.Method{index:D2}()",
                CodeEntityType.Method,
                service.RelativeFilePath,
                service.ProjectId))
            .ToArray();
        snapshot = snapshot with
        {
            Entities = snapshot.Entities.Concat(members).ToArray(),
            Relations = snapshot.Relations.Concat(members.Select(member =>
                ProjectMemoryTestFactory.Contains(service, member))).ToArray()
        };

        var note = new ProjectMemoryBuilder().Build(snapshot).Notes
            .Single(item => item.ManifestEntry.SourceId == service.Id);

        Assert.Contains("Additional members: 18", note.Content, StringComparison.Ordinal);
        Assert.Equal(ProjectMemoryBuilder.MaximumMembersPerComponentNote,
            note.Content.Split('\n').Count(line => line.StartsWith("- Method:", StringComparison.Ordinal)
                || line.StartsWith("- Property:", StringComparison.Ordinal)
                || line.StartsWith("- Class:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Build_IsDeterministicForEquivalentSnapshotKnowledge()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var builder = new ProjectMemoryBuilder();

        var first = builder.Build(snapshot);
        var second = builder.Build(snapshot with
        {
            GeneratedAtUtc = snapshot.GeneratedAtUtc.AddDays(1),
            Projects = snapshot.Projects.Reverse().ToArray(),
            Entities = snapshot.Entities.Reverse().ToArray(),
            Relations = snapshot.Relations.Reverse().ToArray()
        });

        Assert.Equal(ProjectMemoryManifestSerializer.Serialize(first.Manifest), ProjectMemoryManifestSerializer.Serialize(second.Manifest));
        Assert.Equal(
            first.Notes.Select(note => note.Content),
            second.Notes.Select(note => note.Content));
    }

    [Fact]
    public void Build_RejectsIncompatibleSnapshotSchema()
    {
        var snapshot = ProjectMemoryTestFactory.Create() with { SchemaVersion = 2 };

        var exception = Assert.Throws<InvalidDataException>(() => new ProjectMemoryBuilder().Build(snapshot));

        Assert.Contains("requires snapshot schema 3", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("feature/project-memory")]
    [InlineData("release/2026.09")]
    [InlineData("CON")]
    public void BranchKey_IsSafeStableAndPreservesCollisionResistance(string branch)
    {
        var first = KnowledgeIdentity.CreateBranchKey(branch);
        var second = KnowledgeIdentity.CreateBranchKey(branch);

        Assert.Equal(first, second);
        Assert.DoesNotContain('/', first);
        Assert.DoesNotContain('\\', first);
        Assert.Matches("^[A-Za-z0-9._-]+--[a-f0-9]{12}$", first);
    }

    [Fact]
    public void Manifest_RoundTripsIndependentlyFromSnapshotSchema()
    {
        var manifest = new ProjectMemoryBuilder().Build(ProjectMemoryTestFactory.Create()).Manifest;

        var restored = ProjectMemoryManifestSerializer.Deserialize(
            ProjectMemoryManifestSerializer.Serialize(manifest));

        Assert.Equal(1, restored.KnowledgeSchemaVersion);
        Assert.Equal(3, restored.SourceSnapshotSchema);
        Assert.Equal(manifest.Notes, restored.Notes);
    }
}
