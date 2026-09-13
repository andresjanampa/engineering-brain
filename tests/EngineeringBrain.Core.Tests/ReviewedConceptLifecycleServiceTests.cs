using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptLifecycleServiceTests
{
    [Fact]
    public async Task GetStatusAsync_AbsentReturnsZeroCountsAndAbsent()
    {
        using var fixture = new TemporaryDirectory(create: false);

        var result = await Service().GetStatusAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Absent, result.Status);
        Assert.Equal(0, result.DeclarationCount);
        Assert.Equal(0, result.AssignmentCount);
        Assert.Equal(0, result.ResolvedProfileCount);
        Assert.False(Directory.Exists(fixture.Path));
    }

    [Fact]
    public async Task GetStatusAsync_ValidReportsCatalogCountsFingerprintAndProfiles()
    {
        using var fixture = new TemporaryDirectory();
        var catalog = ReviewedConceptTestData.Catalog();
        await WriteAsync(fixture.Path, ReviewedConceptSerializer.Serialize(catalog));

        var result = await Service().GetStatusAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Valid, result.Status);
        Assert.Equal(2, result.DeclarationCount);
        Assert.Equal(2, result.AssignmentCount);
        Assert.Equal(2, result.ResolvedProfileCount);
        Assert.Equal(ReviewedConceptSerializer.CreateCatalogFingerprint(catalog), result.CatalogFingerprint);
    }

    [Fact]
    public async Task GetStatusAsync_MalformedReportsInvalidWithoutThrowing()
    {
        using var fixture = new TemporaryDirectory();
        await WriteAsync(fixture.Path, "{not-json");

        var result = await Service().GetStatusAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Invalid, result.Status);
        Assert.Null(result.DeclarationCount);
        Assert.Null(result.AssignmentCount);
        Assert.Equal(KnowledgeIdentity.ContentHash("{not-json"), result.CatalogFingerprint);
    }

    [Fact]
    public async Task GetStatusAsync_StaleAssignmentReportsValidWithDiagnostics()
    {
        using var fixture = new TemporaryDirectory();
        var catalog = ReviewedConceptTestData.Catalog();
        var stale = catalog.Declarations[0] with
        {
            Assignments =
            [
                catalog.Declarations[0].Assignments[0] with { SourceFingerprint = "stale" }
            ]
        };
        stale = ReviewedConceptTestData.WithFingerprint(stale);
        catalog = catalog with { Declarations = [stale, catalog.Declarations[1]] };
        await WriteAsync(fixture.Path, ReviewedConceptSerializer.Serialize(catalog));

        var result = await Service().GetStatusAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.ValidWithDiagnostics, result.Status);
        Assert.Equal(1, result.InvalidOrStaleAssignmentCount);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC402");
        Assert.Single(result.Diagnostics[0].Message.Split('\n'));
    }

    [Fact]
    public async Task ValidateAsync_BranchMismatchReportsInvalid()
    {
        using var fixture = new TemporaryDirectory();
        var catalog = ReviewedConceptTestData.Catalog() with { Branch = "feature/other" };
        await WriteAsync(fixture.Path, ReviewedConceptSerializer.Serialize(catalog));

        var result = await Service().ValidateAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC102");
    }

    [Fact]
    public async Task ValidateAsync_RepositoryMismatchReportsInvalid()
    {
        using var fixture = new TemporaryDirectory();
        var catalog = ReviewedConceptTestData.Catalog() with { RepositoryId = "repository:other" };
        await WriteAsync(fixture.Path, ReviewedConceptSerializer.Serialize(catalog));

        var result = await Service().ValidateAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC101");
    }

    [Fact]
    public async Task ValidateAsync_DeclarationFingerprintMismatchReportsValidWithDiagnostics()
    {
        using var fixture = new TemporaryDirectory();
        var catalog = ReviewedConceptTestData.Catalog();
        var changed = catalog.Declarations[0] with { Definition = "Changed without re-fingerprinting." };
        catalog = catalog with { Declarations = [changed, catalog.Declarations[1]] };
        await WriteAsync(fixture.Path, ReviewedConceptSerializer.Serialize(catalog));

        var result = await Service().ValidateAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.ValidWithDiagnostics, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC204");
        Assert.Equal(1, result.ResolvedProfileCount);
    }

    [Fact]
    public async Task ValidateAsync_NonCanonicalCatalogReportsValidWithDiagnosticsAndRawFingerprint()
    {
        using var fixture = new TemporaryDirectory();
        var json = " \r\n" + ReviewedConceptSerializer.Serialize(ReviewedConceptTestData.Catalog());
        await WriteAsync(fixture.Path, json);

        var result = await Service().ValidateAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.ValidWithDiagnostics, result.Status);
        Assert.Equal(KnowledgeIdentity.ContentHash(json), result.CatalogFingerprint);
        Assert.Contains(result.Diagnostics, item => item.Code == "RCL103");
    }

    [Fact]
    public async Task ValidateAsync_DirectStructurallyInvalidModelCannotThrow()
    {
        using var fixture = new TemporaryDirectory();
        await WriteAsync(fixture.Path, "{\"schemaVersion\":1}");

        var result = await Service().ValidateAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC001");
    }

    private static ReviewedConceptLifecycleService Service() => new();

    private static ReviewedConceptEvidenceContext Evidence() => ReviewedConceptTestData.Evidence();

    private static async Task WriteAsync(string branchRoot, string content)
    {
        var path = new LocalReviewedConceptStore().GetPath(branchRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(bool create = true)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"engineering-brain-lifecycle-{Guid.NewGuid():N}");
            if (create)
            {
                Directory.CreateDirectory(Path);
            }
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
