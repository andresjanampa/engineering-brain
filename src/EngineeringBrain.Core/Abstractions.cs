namespace EngineeringBrain.Core;

public interface IRepositoryScanner
{
    Task<RepositoryScanResult> ScanAsync(string path, CancellationToken cancellationToken = default);
}

public interface IGitInfoProvider
{
    Task<GitInfo> GetInfoAsync(string repositoryRoot, CancellationToken cancellationToken = default);
}

public interface ILanguageAnalyzer
{
    string Language { get; }

    Task<LanguageAnalysisResult> AnalyzeAsync(
        LanguageAnalysisRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRepositorySnapshotStore
{
    Task<string> SaveAsync(RepositorySnapshot snapshot, CancellationToken cancellationToken = default);
}
