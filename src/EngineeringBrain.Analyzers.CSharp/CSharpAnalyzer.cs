using EngineeringBrain.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace EngineeringBrain.Analyzers.CSharp;

public sealed class CSharpAnalyzer : ILanguageAnalyzer
{
    public const string AnalyzerVersion = "csharp-project-aware-v2";

    public string Language => "C#";

    public async Task<LanguageAnalysisResult> AnalyzeAsync(
        LanguageAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        var discovery = CSharpProjectDiscovery.Discover(request);
        var load = await MSBuildProjectLoader.LoadAsync(discovery, request.RepositoryRoot, cancellationToken);
        var diagnostics = discovery.Diagnostics.Concat(load.Diagnostics).ToList();
        var projectDiagnostics = discovery.Projects.ToDictionary(
            project => project.RelativePath,
            project => load.ProjectDiagnostics[project.RelativePath].ToList(),
            StringComparer.OrdinalIgnoreCase);

        var projectEntities = new Dictionary<string, CodeEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in discovery.Projects)
        {
            projectEntities[project.RelativePath] = await CreateFileEntityAsync(
                request.RepositoryRoot,
                project.Id,
                project.Name,
                project.RelativePath,
                CodeEntityType.Project,
                project.Id,
                cancellationToken);
        }

        var solutionEntities = new Dictionary<string, CodeEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var solution in discovery.Solutions)
        {
            solutionEntities[solution.RelativePath] = await CreateFileEntityAsync(
                request.RepositoryRoot,
                solution.Id,
                solution.Name,
                solution.RelativePath,
                CodeEntityType.Solution,
                null,
                cancellationToken);
        }

        var entities = projectEntities.Values.Concat(solutionEntities.Values).ToList();
        var relations = CreateSolutionRelations(load, solutionEntities, projectEntities).ToList();
        var allowedFiles = request.Files
            .Where(file => file.Extension == ".cs")
            .Select(file => file.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loadedByPath = load.LoadedProjects.ToDictionary(
            context => context.Discovery.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var semanticExtractions = new List<SemanticExtraction>();
        var projectDocuments = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var context in load.LoadedProjects)
        {
            var extraction = await CSharpSymbolExtractor.ExtractSemanticAsync(
                context,
                projectEntities[context.Discovery.RelativePath],
                request.RepositoryRoot,
                allowedFiles,
                cancellationToken);
            semanticExtractions.Add(extraction);
            entities.AddRange(extraction.Entities);
            relations.AddRange(extraction.Relations);
            projectDocuments[context.Discovery.RelativePath] = extraction.Documents;
        }

        var typeRelations = CSharpSymbolExtractor.ResolveTypeRelations(
            semanticExtractions,
            request.RepositoryRoot,
            cancellationToken);
        relations.AddRange(typeRelations.Relations);
        diagnostics.AddRange(typeRelations.Diagnostics);
        foreach (var diagnostic in typeRelations.Diagnostics.Where(item => item.ProjectPath is not null))
        {
            projectDiagnostics[diagnostic.ProjectPath!].Add(diagnostic);
        }

        var fallbackProjects = discovery.Projects
            .Where(project => !loadedByPath.ContainsKey(project.RelativePath))
            .ToArray();
        foreach (var project in fallbackProjects)
        {
            var files = GetOwnedFiles(
                request.RepositoryRoot,
                project,
                discovery.Projects,
                request.Files);
            var extraction = await CSharpSymbolExtractor.ExtractSyntaxAsync(
                request.RepositoryRoot,
                files,
                projectEntities[project.RelativePath],
                project.Id,
                cancellationToken);
            entities.AddRange(extraction.Entities);
            relations.AddRange(extraction.Relations);
            projectDocuments[project.RelativePath] = extraction.Documents;

            if (extraction.UnresolvedBaseCount > 0)
            {
                var diagnostic = new AnalysisDiagnostic(
                    "CSHARP_FALLBACK_BASE_TYPES_OMITTED",
                    AnalysisDiagnosticSeverity.Information,
                    $"{extraction.UnresolvedBaseCount} base-type declaration(s) were not emitted as relationships because semantic resolution was unavailable.",
                    project.RelativePath);
                diagnostics.Add(diagnostic);
                projectDiagnostics[project.RelativePath].Add(diagnostic);
            }
        }

        var ownedPaths = discovery.Projects
            .SelectMany(project => GetOwnedFiles(
                request.RepositoryRoot,
                project,
                discovery.Projects,
                request.Files))
            .Select(file => file.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var looseFiles = request.Files
            .Where(file => file.Extension == ".cs" && !ownedPaths.Contains(file.RelativePath))
            .ToArray();
        if (looseFiles.Length > 0)
        {
            var looseExtraction = await CSharpSymbolExtractor.ExtractSyntaxAsync(
                request.RepositoryRoot,
                looseFiles,
                null,
                null,
                cancellationToken);
            entities.AddRange(looseExtraction.Entities);
            relations.AddRange(looseExtraction.Relations);
            diagnostics.Add(new AnalysisDiagnostic(
                "CSHARP_LOOSE_FILES_SYNTAX_ONLY",
                AnalysisDiagnosticSeverity.Information,
                $"{looseFiles.Length} C# file(s) outside a discovered project were analyzed syntactically.",
                null));
        }

        var projectReferenceResult = CreateProjectReferenceRelations(
            discovery,
            loadedByPath,
            projectEntities);
        relations.AddRange(projectReferenceResult.Relations);

        var projects = discovery.Projects.Select(project =>
        {
            var isSemantic = loadedByPath.ContainsKey(project.RelativePath);
            var references = projectReferenceResult.ReferencesByProject[project.RelativePath];
            return new EngineeringBrain.Core.ProjectInfo(
                project.Id,
                project.Name,
                project.RelativePath,
                "C#",
                project.TargetFrameworks,
                references,
                projectDocuments.GetValueOrDefault(project.RelativePath) ?? [],
                isSemantic ? ProjectAnalysisMode.Semantic : ProjectAnalysisMode.SyntaxFallback,
                projectDiagnostics[project.RelativePath]);
        }).ToArray();

        var semanticCount = projects.Count(project => project.AnalysisMode == ProjectAnalysisMode.Semantic);
        var fallbackCount = projects.Length - semanticCount;
        var hasUnattributedLoadFailure = diagnostics.Any(diagnostic =>
            diagnostic.ProjectPath is null
            && diagnostic.Severity == AnalysisDiagnosticSeverity.Warning
            && diagnostic.Code is "CSHARP_MSBUILD_DIAGNOSTIC" or "CSHARP_SOLUTION_LOAD_FAILED");
        var mode = semanticCount == projects.Length && projects.Length > 0 && !hasUnattributedLoadFailure
            ? AnalysisMode.FullSemantic
            : semanticCount > 0
                ? AnalysisMode.PartialSemantic
                : AnalysisMode.SyntaxFallback;
        var summary = new AnalysisSummary(
            mode,
            AnalyzerVersion,
            projects.Length,
            semanticCount,
            fallbackCount);

        return new LanguageAnalysisResult(
            entities.OrderBy(entity => entity.RelativeFilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entity => entity.StartLine)
                .ThenBy(entity => entity.EntityType)
                .ToArray(),
            DeduplicateRelations(relations),
            projects,
            summary,
            diagnostics);
    }

    private static IReadOnlyList<ScannedFile> GetOwnedFiles(
        string repositoryRoot,
        DiscoveredProject project,
        IReadOnlyList<DiscoveredProject> allProjects,
        IReadOnlyList<ScannedFile> files)
    {
        return files.Where(file => file.Extension == ".cs")
            .Where(file =>
            {
                var fullPath = CSharpProjectDiscovery.ToFullPath(repositoryRoot, file.RelativePath);
                var owner = allProjects
                    .Where(candidate => IsUnderDirectory(Path.GetDirectoryName(candidate.FullPath)!, fullPath))
                    .OrderByDescending(candidate => Path.GetDirectoryName(candidate.FullPath)!.Length)
                    .FirstOrDefault();
                return owner?.RelativePath.Equals(project.RelativePath, StringComparison.OrdinalIgnoreCase) == true;
            })
            .ToArray();
    }

    private static bool IsUnderDirectory(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static ProjectReferenceResult CreateProjectReferenceRelations(
        ProjectDiscoveryResult discovery,
        IReadOnlyDictionary<string, LoadedProjectContext> loadedByPath,
        IReadOnlyDictionary<string, CodeEntity> projectEntities)
    {
        var relations = new List<CodeRelation>();
        var referencesByProject = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var discoveredByFullPath = discovery.Projects.ToDictionary(
            project => Path.GetFullPath(project.FullPath),
            project => project,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var project in discovery.Projects)
        {
            var references = new List<(DiscoveredProject Target, int Line, ResolutionLevel Level)>();
            if (loadedByPath.TryGetValue(project.RelativePath, out var loaded))
            {
                foreach (var reference in loaded.Project.ProjectReferences)
                {
                    var target = loaded.Project.Solution.GetProject(reference.ProjectId);
                    if (target?.FilePath is null
                        || !discoveredByFullPath.TryGetValue(Path.GetFullPath(target.FilePath), out var discoveredTarget))
                    {
                        continue;
                    }

                    var evidence = project.ProjectReferences.FirstOrDefault(item =>
                        item.RelativePath.Equals(discoveredTarget.RelativePath, StringComparison.OrdinalIgnoreCase));
                    references.Add((discoveredTarget, evidence?.Line ?? 1, ResolutionLevel.Exact));
                }
            }
            else
            {
                foreach (var reference in project.ProjectReferences)
                {
                    var target = discovery.Projects.FirstOrDefault(candidate =>
                        candidate.RelativePath.Equals(reference.RelativePath, StringComparison.OrdinalIgnoreCase));
                    if (target is not null)
                    {
                        references.Add((target, reference.Line, ResolutionLevel.Syntactic));
                    }
                }
            }

            referencesByProject[project.RelativePath] = references
                .Select(item => item.Target.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            relations.AddRange(references.Select(reference => new CodeRelation(
                projectEntities[project.RelativePath].Id,
                projectEntities[reference.Target.RelativePath].Id,
                CodeRelationType.ReferencesProject,
                project.RelativePath,
                reference.Line,
                reference.Line,
                reference.Level)));
        }

        return new ProjectReferenceResult(relations, referencesByProject);
    }

    private static IEnumerable<CodeRelation> CreateSolutionRelations(
        MSBuildLoadResult load,
        IReadOnlyDictionary<string, CodeEntity> solutionEntities,
        IReadOnlyDictionary<string, CodeEntity> projectEntities)
    {
        foreach (var membership in load.SolutionMembership)
        {
            if (!solutionEntities.TryGetValue(membership.Key, out var solution))
            {
                continue;
            }

            foreach (var projectPath in membership.Value)
            {
                if (projectEntities.TryGetValue(projectPath, out var project))
                {
                    yield return new CodeRelation(
                        solution.Id,
                        project.Id,
                        CodeRelationType.Contains,
                        membership.Key,
                        1,
                        1,
                        ResolutionLevel.Exact);
                }
            }
        }
    }

    private static async Task<CodeEntity> CreateFileEntityAsync(
        string repositoryRoot,
        string id,
        string name,
        string relativePath,
        CodeEntityType type,
        string? projectId,
        CancellationToken cancellationToken)
    {
        var endLine = 1;
        try
        {
            var text = await File.ReadAllTextAsync(
                CSharpProjectDiscovery.ToFullPath(repositoryRoot, relativePath),
                cancellationToken);
            endLine = SourceText.From(text).Lines.Count;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return new CodeEntity(
            id,
            name,
            name,
            type,
            "C#",
            relativePath,
            1,
            endLine,
            projectId,
            ResolutionLevel.Exact,
            []);
    }

    private static IReadOnlyList<CodeRelation> DeduplicateRelations(IEnumerable<CodeRelation> relations) =>
        relations.GroupBy(relation => new
        {
            relation.SourceEntityId,
            relation.TargetEntityId,
            relation.RelationType
        })
            .Select(group => group.OrderBy(relation => relation.ResolutionLevel)
                .ThenBy(relation => relation.RelativeFilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(relation => relation.StartLine)
                .First())
            .OrderBy(relation => relation.RelativeFilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(relation => relation.StartLine)
            .ThenBy(relation => relation.RelationType)
            .ToArray();

    private sealed record ProjectReferenceResult(
        IReadOnlyList<CodeRelation> Relations,
        IReadOnlyDictionary<string, IReadOnlyList<string>> ReferencesByProject);
}
