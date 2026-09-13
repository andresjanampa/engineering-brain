using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class LocalReviewedConceptStore
{
    public string GetPath(string branchKnowledgeLocation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchKnowledgeLocation);

        var branchRoot = Path.GetFullPath(branchKnowledgeLocation);
        var path = Path.GetFullPath(Path.Combine(branchRoot, "semantic", "reviewed-concepts.json"));
        var rootPrefix = branchRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Reviewed concept path escapes the branch knowledge location.");
        }

        return path;
    }

    public async Task<ReviewedConceptLoadResult> LoadAsync(
        string branchKnowledgeLocation,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(branchKnowledgeLocation);
        if (!File.Exists(path))
        {
            return new ReviewedConceptLoadResult(
                ReviewedConceptLoadStatus.Absent,
                path,
                null,
                null,
                []);
        }

        string? contentHash = null;
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            contentHash = KnowledgeIdentity.ContentHash(json);
            var catalog = ReviewedConceptSerializer.Deserialize(json);
            return new ReviewedConceptLoadResult(
                ReviewedConceptLoadStatus.Loaded,
                path,
                contentHash,
                catalog,
                []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException)
        {
            return new ReviewedConceptLoadResult(
                ReviewedConceptLoadStatus.Invalid,
                path,
                contentHash,
                null,
                [new ReviewedConceptDiagnostic(
                    "RC001",
                    AnalysisDiagnosticSeverity.Error,
                    ReviewedConceptDiagnosticScope.Catalog,
                    "Reviewed concept catalog could not be read or parsed safely.")]);
        }
    }
}
