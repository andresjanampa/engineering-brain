namespace EngineeringBrain.Core;

public interface IRepositoryScanner
{
    Task<RepositoryScanResult> ScanAsync(string path, CancellationToken cancellationToken = default);
}

public interface IGitInfoProvider
{
    Task<GitInfo> GetInfoAsync(string repositoryRoot, CancellationToken cancellationToken = default);
}

public interface IGitChangeProvider
{
    Task<IReadOnlyList<GitRename>> GetRenamesAsync(
        string repositoryRoot,
        string? previousCommit,
        string? currentCommit,
        CancellationToken cancellationToken = default);
}

public interface ILanguageAnalyzer
{
    string Language { get; }

    string Version { get; }

    Task<LanguageAnalysisResult> AnalyzeAsync(
        LanguageAnalysisRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRepositorySnapshotStore
{
    Task<SnapshotLoadResult> LoadLatestAsync(
        string repositoryId,
        CancellationToken cancellationToken = default);

    Task<string> SaveAsync(RepositorySnapshot snapshot, CancellationToken cancellationToken = default);
}

public interface IReasoningProvider
{
    string Name { get; }

    Task<ReasoningResult<T>> GenerateStructuredAsync<T>(
        ReasoningRequest request,
        CancellationToken cancellationToken = default);
}
