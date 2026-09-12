namespace EngineeringBrain.Core;

public sealed record RepositoryInfo(string Id, string Name, string Root);

public sealed record GitInfo(
    bool IsRepository,
    string? Branch,
    string? HeadCommit,
    string? Remote,
    bool? IsWorkingTreeClean);

public sealed record LanguageStatistics(string Language, int FileCount, long TotalBytes);

public sealed record ScannedFile(
    string RelativePath,
    string Extension,
    string Language,
    long SizeBytes,
    string? ContentHash);

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
    IReadOnlyList<ScannedFile> Files);

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
    IReadOnlyList<AnalysisDiagnostic> Diagnostics);
