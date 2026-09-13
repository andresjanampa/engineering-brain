using System.Text.Json.Serialization;

namespace EngineeringBrain.Core;

public sealed record RepositoryInfo(string Id, string Name, string Root);

public sealed record GitInfo(
    bool IsRepository,
    string? Branch,
    string? HeadCommit,
    string? Remote,
    bool? IsWorkingTreeClean)
{
    public const string DetachedHeadBranch = "(detached HEAD)";

    [JsonIgnore]
    public bool IsDetachedHead => string.Equals(Branch, DetachedHeadBranch, StringComparison.Ordinal);
}

public sealed record LanguageStatistics(string Language, int FileCount, long TotalBytes);

public sealed record ScannedFile(
    string RelativePath,
    string Extension,
    string Language,
    long SizeBytes,
    string? ContentHash,
    DateTimeOffset LastWriteTimeUtc = default);

public sealed record SourceLocation(
    string RelativeFilePath,
    int StartLine,
    int EndLine);

public sealed record AnalysisDiagnostic(
    string Code,
    AnalysisDiagnosticSeverity Severity,
    string Message,
    string? ProjectPath);

public sealed record ProjectInfo(
    string Id,
    string Name,
    string RelativePath,
    string Language,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> Documents,
    ProjectAnalysisMode AnalysisMode,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics);

public sealed record AnalysisSummary(
    AnalysisMode Mode,
    string AnalyzerVersion,
    int DetectedProjects,
    int SemanticProjects,
    int FallbackProjects);

public sealed record CodeEntity(
    string Id,
    string Name,
    string FullName,
    CodeEntityType EntityType,
    string Language,
    string RelativeFilePath,
    int StartLine,
    int EndLine,
    string? ProjectId,
    ResolutionLevel ResolutionLevel,
    IReadOnlyList<SourceLocation> AdditionalLocations);

public sealed record CodeRelation(
    string SourceEntityId,
    string TargetEntityId,
    CodeRelationType RelationType,
    string RelativeFilePath,
    int StartLine,
    int EndLine,
    ResolutionLevel ResolutionLevel);

public sealed record RepositoryScanResult(
    RepositoryInfo Repository,
    IReadOnlyList<ScannedFile> Files,
    IReadOnlyList<LanguageStatistics> Languages);

public sealed record LanguageAnalysisRequest(
    string RepositoryRoot,
    IReadOnlyList<ScannedFile> Files,
    IReadOnlyList<string>? IncludedProjectPaths = null,
    IReadOnlyList<CodeEntity>? ReusableEntities = null);

public sealed record LanguageAnalysisResult(
    IReadOnlyList<CodeEntity> Entities,
    IReadOnlyList<CodeRelation> Relations,
    IReadOnlyList<ProjectInfo> Projects,
    AnalysisSummary Analysis,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics);

public sealed record RepositorySnapshot(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    RepositoryInfo Repository,
    GitInfo Git,
    IReadOnlyList<ScannedFile> Files,
    IReadOnlyList<LanguageStatistics> Languages,
    IReadOnlyList<ProjectInfo> Projects,
    IReadOnlyList<CodeEntity> Entities,
    IReadOnlyList<CodeRelation> Relations,
    AnalysisSummary Analysis,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics,
    IncrementalAnalysisSummary Incremental);

public sealed record FileChange(
    FileChangeKind Kind,
    string? CurrentPath,
    string? PreviousPath,
    ChangeDetectionMethod DetectionMethod,
    string? ProjectPath,
    string Reason);

public sealed record ScanPerformanceMetrics(
    int TotalFiles,
    int ChangedFiles,
    int ProjectsTotal,
    int ProjectsAnalyzed,
    int ProjectsReused,
    int EntitiesReused,
    int EntitiesRegenerated,
    long ElapsedMilliseconds);

public sealed record GraphIntegritySummary(
    bool IsValid,
    int DuplicateEntityIds,
    int DuplicateRelations,
    int DanglingRelations);

public sealed record IncrementalAnalysisSummary(
    ScanExecutionMode Mode,
    bool PreviousSnapshotFound,
    string? FullScanReason,
    IReadOnlyList<FileChange> Changes,
    IReadOnlyList<string> DirectlyAffectedProjects,
    IReadOnlyList<string> TransitivelyAffectedProjects,
    IReadOnlyList<string> ReanalyzedProjects,
    IReadOnlyList<string> ReusedProjects,
    ScanPerformanceMetrics Metrics,
    GraphIntegritySummary GraphIntegrity);

public sealed record RepositoryAnalysisResult(
    RepositorySnapshot Snapshot,
    string SnapshotPath);

public sealed record GitRename(string PreviousPath, string CurrentPath);

public sealed record SnapshotLoadResult(
    SnapshotLoadStatus Status,
    RepositorySnapshot? Snapshot,
    string Path,
    string Reason);
