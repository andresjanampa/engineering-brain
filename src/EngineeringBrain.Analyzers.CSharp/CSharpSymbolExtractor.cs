using EngineeringBrain.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace EngineeringBrain.Analyzers.CSharp;

internal static class CSharpSymbolExtractor
{
    private const int MaximumUnresolvedDiagnosticsPerProject = 25;

    private static readonly SymbolDisplayFormat DisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType
            | SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static async Task<SemanticExtraction> ExtractSemanticAsync(
        LoadedProjectContext context,
        CodeEntity projectEntity,
        string repositoryRoot,
        IReadOnlySet<string> allowedFiles,
        CancellationToken cancellationToken)
    {
        var declarations = new List<DeclarationEvidence>();
        var documents = new List<string>();

        foreach (var document in context.Project.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document.FilePath is null || !TryGetRepositoryPath(repositoryRoot, document.FilePath, out var relativePath))
            {
                continue;
            }

            relativePath = NormalizePath(relativePath);
            if (!allowedFiles.Contains(relativePath))
            {
                continue;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            if (root is null || !context.Compilation.ContainsSyntaxTree(root.SyntaxTree))
            {
                continue;
            }

            documents.Add(relativePath);
            var model = context.Compilation.GetSemanticModel(root.SyntaxTree, ignoreAccessibility: true);
            foreach (var declaration in root.DescendantNodes().Where(IsSupportedDeclaration))
            {
                var symbol = model.GetDeclaredSymbol(declaration, cancellationToken);
                var identity = symbol is null
                    ? $"syntax:{relativePath}:{declaration.SpanStart}"
                    : GetSymbolIdentity(symbol);
                declarations.Add(new DeclarationEvidence(
                    declaration,
                    symbol,
                    model,
                    identity,
                    CreateLocation(declaration, relativePath)));
            }
        }

        return BuildSemanticExtraction(
            context,
            projectEntity,
            declarations,
            documents,
            cancellationToken);
    }

    public static async Task<SyntaxExtraction> ExtractSyntaxAsync(
        string repositoryRoot,
        IReadOnlyList<ScannedFile> files,
        CodeEntity? projectEntity,
        string? projectId,
        CancellationToken cancellationToken)
    {
        var declarations = new List<DeclarationEvidence>();
        var unresolvedBaseCount = 0;

        foreach (var file in files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = CSharpProjectDiscovery.ToFullPath(repositoryRoot, file.RelativePath);

            try
            {
                var text = await File.ReadAllTextAsync(fullPath, cancellationToken);
                var tree = CSharpSyntaxTree.ParseText(
                    SourceText.From(text),
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                    file.RelativePath,
                    cancellationToken: cancellationToken);
                var root = await tree.GetRootAsync(cancellationToken);

                foreach (var declaration in root.DescendantNodes().Where(IsSupportedDeclaration))
                {
                    var fullName = GetSyntacticFullName(declaration);
                    var isPartialType = declaration is TypeDeclarationSyntax typeDeclaration
                        && typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword);
                    var identity = isPartialType
                        ? $"partial:{GetEntityType(declaration)}:{fullName}"
                        : $"syntax:{file.RelativePath}:{declaration.SpanStart}:{fullName}";
                    declarations.Add(new DeclarationEvidence(
                        declaration,
                        null,
                        null,
                        identity,
                        CreateLocation(declaration, file.RelativePath)));

                    if (declaration is BaseTypeDeclarationSyntax { BaseList.Types.Count: > 0 })
                    {
                        unresolvedBaseCount++;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        var entities = new List<CodeEntity>();
        var entityByNode = new Dictionary<SyntaxNode, CodeEntity>();

        foreach (var group in declarations.GroupBy(declaration => declaration.Identity, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(item => item.Location.RelativeFilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Location.StartLine)
                .ToArray();
            var primary = ordered[0];
            var type = GetEntityType(primary.Node);
            var fullName = GetSyntacticFullName(primary.Node);
            var syntacticIdentity = primary.Identity.StartsWith("partial:", StringComparison.Ordinal)
                ? $"syntactic:{fullName}"
                : $"syntactic:{fullName}|{primary.Location.RelativeFilePath}";
            var id = projectId is null
                ? StableEntityId.Create("C#", type, primary.Location.RelativeFilePath, fullName)
                : StableEntityId.CreateSemantic("C#", projectId, type, syntacticIdentity);
            var entity = new CodeEntity(
                id,
                GetName(primary.Node),
                fullName,
                type,
                "C#",
                primary.Location.RelativeFilePath,
                primary.Location.StartLine,
                primary.Location.EndLine,
                projectId,
                ResolutionLevel.Syntactic,
                ordered.Skip(1).Select(item => item.Location).ToArray());
            entities.Add(entity);
            foreach (var declaration in ordered)
            {
                entityByNode[declaration.Node] = entity;
            }
        }

        var relations = BuildContainmentRelations(declarations, entityByNode, projectEntity);
        return new SyntaxExtraction(
            entities,
            relations,
            files.Select(file => file.RelativePath).ToArray(),
            unresolvedBaseCount);
    }

    public static TypeRelationResult ResolveTypeRelations(
        IReadOnlyList<SemanticExtraction> extractions,
        IReadOnlyList<LoadedProjectContext> loadedProjects,
        IReadOnlyList<CodeEntity> reusableEntities,
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var relations = new List<CodeRelation>();
        var diagnostics = new List<AnalysisDiagnostic>();
        var entityByProjectAndIdentity = extractions
            .SelectMany(extraction => extraction.EntityByIdentity.Select(pair =>
                new KeyValuePair<SemanticEntityKey, CodeEntity>(
                    new SemanticEntityKey(extraction.ProjectId, pair.Key),
                    pair.Value)))
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.First().Value);
        var projectIdsByAssembly = loadedProjects
            .GroupBy(context => context.Compilation.Assembly.Identity.ToString(), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Discovery.Id).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var reusableById = reusableEntities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);

        foreach (var extraction in extractions)
        {
            var unresolvedCount = 0;
            foreach (var evidence in extraction.BaseTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetSymbol = evidence.Model.GetTypeInfo(evidence.BaseType.Type, cancellationToken).Type?.OriginalDefinition;
                if (targetSymbol is null || targetSymbol.TypeKind == TypeKind.Error)
                {
                    AddUnresolvedDiagnostic(extraction, evidence, diagnostics, ref unresolvedCount);
                    continue;
                }

                CodeEntity? targetEntity = null;
                if (!extraction.EntityBySymbol.TryGetValue(targetSymbol, out targetEntity))
                {
                    var assemblyIdentity = targetSymbol.ContainingAssembly?.Identity.ToString();
                    if (assemblyIdentity is not null
                        && projectIdsByAssembly.TryGetValue(assemblyIdentity, out var projectIds)
                        && projectIds.Length == 1)
                    {
                        entityByProjectAndIdentity.TryGetValue(
                            new SemanticEntityKey(projectIds[0], GetSymbolIdentity(targetSymbol)),
                            out targetEntity);
                        if (targetEntity is null
                            && TryGetEntityType(targetSymbol, out var targetEntityType))
                        {
                            var targetId = StableEntityId.CreateSemantic(
                                "C#",
                                projectIds[0],
                                targetEntityType,
                                GetSymbolIdentity(targetSymbol));
                            reusableById.TryGetValue(targetId, out targetEntity);
                        }
                    }
                }

                if (targetEntity is null)
                {
                    AddUnresolvedDiagnostic(extraction, evidence, diagnostics, ref unresolvedCount);
                    continue;
                }

                var relationType = evidence.Source.EntityType == CodeEntityType.Interface
                    || targetSymbol.TypeKind != TypeKind.Interface
                        ? CodeRelationType.Inherits
                        : CodeRelationType.Implements;
                relations.Add(CreateRelation(
                    evidence.Source,
                    targetEntity,
                    relationType,
                    CreateLocation(evidence.BaseType, evidence.RelativePath),
                    ResolutionLevel.Semantic));
            }
        }

        return new TypeRelationResult(DeduplicateRelations(relations), diagnostics);
    }

    private static SemanticExtraction BuildSemanticExtraction(
        LoadedProjectContext context,
        CodeEntity projectEntity,
        IReadOnlyList<DeclarationEvidence> declarations,
        IReadOnlyList<string> documents,
        CancellationToken cancellationToken)
    {
        var entities = new List<CodeEntity>();
        var entityByNode = new Dictionary<SyntaxNode, CodeEntity>();
        var entityBySymbol = new Dictionary<ISymbol, CodeEntity>(SymbolEqualityComparer.Default);
        var entityByIdentity = new Dictionary<string, CodeEntity>(StringComparer.Ordinal);

        foreach (var group in declarations.GroupBy(declaration => declaration.Identity, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordered = group.OrderBy(item => item.Location.RelativeFilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Location.StartLine)
                .ToArray();
            var primary = ordered[0];
            var type = GetEntityType(primary.Node);
            var fullName = primary.Symbol is null
                ? GetSyntacticFullName(primary.Node)
                : primary.Symbol.ToDisplayString(DisplayFormat);
            var resolution = primary.Symbol is null ? ResolutionLevel.Syntactic : ResolutionLevel.Semantic;
            var id = primary.Symbol is null
                ? StableEntityId.Create("C#", type, primary.Location.RelativeFilePath, fullName)
                : StableEntityId.CreateSemantic("C#", context.Discovery.Id, type, primary.Identity);
            var entity = new CodeEntity(
                id,
                GetName(primary.Node),
                fullName,
                type,
                "C#",
                primary.Location.RelativeFilePath,
                primary.Location.StartLine,
                primary.Location.EndLine,
                context.Discovery.Id,
                resolution,
                ordered.Skip(1).Select(item => item.Location).ToArray());
            entities.Add(entity);
            entityByIdentity.TryAdd(primary.Identity, entity);

            foreach (var declaration in ordered)
            {
                entityByNode[declaration.Node] = entity;
                if (declaration.Symbol is not null)
                {
                    entityBySymbol.TryAdd(declaration.Symbol, entity);
                }
            }
        }

        var containment = BuildContainmentRelations(declarations, entityByNode, projectEntity);
        var baseTypes = declarations
            .Where(declaration => declaration.Node is BaseTypeDeclarationSyntax { BaseList: not null })
            .SelectMany(declaration =>
                ((BaseTypeDeclarationSyntax)declaration.Node).BaseList!.Types.Select(baseType =>
                    new BaseTypeEvidence(
                        entityByNode[declaration.Node],
                        baseType,
                        declaration.Model!,
                        declaration.Location.RelativeFilePath)))
            .ToArray();

        return new SemanticExtraction(
            context.Discovery.Id,
            context.Discovery.RelativePath,
            context.Compilation.Assembly.Identity.ToString(),
            entities,
            containment,
            documents.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            entityBySymbol,
            entityByIdentity,
            baseTypes);
    }

    private static IReadOnlyList<CodeRelation> BuildContainmentRelations(
        IReadOnlyList<DeclarationEvidence> declarations,
        IReadOnlyDictionary<SyntaxNode, CodeEntity> entityByNode,
        CodeEntity? projectEntity)
    {
        var relations = new List<CodeRelation>();

        foreach (var declaration in declarations.OrderBy(item => item.Location.RelativeFilePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Location.StartLine))
        {
            var target = entityByNode[declaration.Node];
            var parentNode = declaration.Node.Ancestors().FirstOrDefault(entityByNode.ContainsKey);
            var source = parentNode is null ? projectEntity : entityByNode[parentNode];
            if (source is null || source.Id == target.Id)
            {
                continue;
            }

            var resolution = declaration.Symbol is null
                ? ResolutionLevel.Syntactic
                : ResolutionLevel.Semantic;
            relations.Add(CreateRelation(
                source,
                target,
                CodeRelationType.Contains,
                declaration.Location,
                resolution));
        }

        return DeduplicateRelations(relations);
    }

    private static IReadOnlyList<CodeRelation> DeduplicateRelations(IEnumerable<CodeRelation> relations) =>
        relations.GroupBy(relation => new
        {
            relation.SourceEntityId,
            relation.TargetEntityId,
            relation.RelationType
        })
            .Select(group => group.OrderBy(relation => relation.RelativeFilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(relation => relation.StartLine)
                .First())
            .ToArray();

    private static void AddUnresolvedDiagnostic(
        SemanticExtraction extraction,
        BaseTypeEvidence evidence,
        ICollection<AnalysisDiagnostic> diagnostics,
        ref int unresolvedCount)
    {
        if (unresolvedCount++ >= MaximumUnresolvedDiagnosticsPerProject)
        {
            return;
        }

        var line = evidence.BaseType.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        diagnostics.Add(new AnalysisDiagnostic(
            "CSHARP_BASE_TYPE_UNRESOLVED",
            AnalysisDiagnosticSeverity.Information,
            $"A base type at line {line} could not be mapped to a source entity; no relationship was emitted.",
            extraction.ProjectPath));
    }

    private static CodeRelation CreateRelation(
        CodeEntity source,
        CodeEntity target,
        CodeRelationType relationType,
        SourceLocation evidence,
        ResolutionLevel resolutionLevel) =>
        new(
            source.Id,
            target.Id,
            relationType,
            evidence.RelativeFilePath,
            evidence.StartLine,
            evidence.EndLine,
            resolutionLevel);

    private static SourceLocation CreateLocation(SyntaxNode node, string relativePath)
    {
        var lines = node.GetLocation().GetLineSpan();
        return new SourceLocation(
            NormalizePath(relativePath),
            lines.StartLinePosition.Line + 1,
            lines.EndLinePosition.Line + 1);
    }

    private static bool TryGetRepositoryPath(string repositoryRoot, string fullPath, out string relativePath)
    {
        relativePath = Path.GetRelativePath(repositoryRoot, fullPath);
        return relativePath != ".."
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relativePath);
    }

    private static bool IsSupportedDeclaration(SyntaxNode node) => node is
        BaseNamespaceDeclarationSyntax
        or ClassDeclarationSyntax
        or InterfaceDeclarationSyntax
        or RecordDeclarationSyntax
        or EnumDeclarationSyntax
        or MethodDeclarationSyntax
        or ConstructorDeclarationSyntax
        or PropertyDeclarationSyntax;

    private static CodeEntityType GetEntityType(SyntaxNode declaration) => declaration switch
    {
        BaseNamespaceDeclarationSyntax => CodeEntityType.Namespace,
        ClassDeclarationSyntax => CodeEntityType.Class,
        InterfaceDeclarationSyntax => CodeEntityType.Interface,
        RecordDeclarationSyntax => CodeEntityType.Record,
        EnumDeclarationSyntax => CodeEntityType.Enum,
        MethodDeclarationSyntax => CodeEntityType.Method,
        ConstructorDeclarationSyntax => CodeEntityType.Constructor,
        PropertyDeclarationSyntax => CodeEntityType.Property,
        _ => throw new ArgumentOutOfRangeException(nameof(declaration))
    };

    private static bool TryGetEntityType(ITypeSymbol symbol, out CodeEntityType entityType)
    {
        entityType = symbol.TypeKind switch
        {
            TypeKind.Interface => CodeEntityType.Interface,
            TypeKind.Enum => CodeEntityType.Enum,
            TypeKind.Class when symbol is INamedTypeSymbol { IsRecord: true } => CodeEntityType.Record,
            TypeKind.Class => CodeEntityType.Class,
            _ => default
        };
        return symbol.TypeKind is TypeKind.Interface or TypeKind.Enum or TypeKind.Class;
    }

    private static string GetName(SyntaxNode declaration) => declaration switch
    {
        BaseNamespaceDeclarationSyntax namespaceDeclaration => namespaceDeclaration.Name.ToString(),
        BaseTypeDeclarationSyntax typeDeclaration => typeDeclaration.Identifier.ValueText,
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        _ => throw new ArgumentOutOfRangeException(nameof(declaration))
    };

    private static string GetSymbolIdentity(ISymbol symbol)
    {
        symbol = symbol switch
        {
            IMethodSymbol { PartialDefinitionPart: not null } method => method.PartialDefinitionPart,
            IMethodSymbol { PartialImplementationPart: not null } method => method,
            _ => symbol.OriginalDefinition
        };

        return symbol.GetDocumentationCommentId()
            ?? $"{symbol.Kind}:{symbol.ToDisplayString(DisplayFormat)}";
    }

    private static string GetSyntacticFullName(SyntaxNode declaration)
    {
        var prefix = string.Join(
            ".",
            declaration.Ancestors()
                .Where(node => node is BaseNamespaceDeclarationSyntax or BaseTypeDeclarationSyntax)
                .Reverse()
                .Select(GetSyntacticName));
        var name = GetSyntacticName(declaration);

        return string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";
    }

    private static string GetSyntacticName(SyntaxNode declaration) => declaration switch
    {
        BaseNamespaceDeclarationSyntax namespaceDeclaration => namespaceDeclaration.Name.ToString(),
        TypeDeclarationSyntax type => type.TypeParameterList is null
            ? type.Identifier.ValueText
            : $"{type.Identifier.ValueText}`{type.TypeParameterList.Parameters.Count}",
        EnumDeclarationSyntax @enum => @enum.Identifier.ValueText,
        MethodDeclarationSyntax method =>
            $"{method.Identifier.ValueText}`{method.TypeParameterList?.Parameters.Count ?? 0}({string.Join(",", method.ParameterList.Parameters.Select(parameter => parameter.Type?.ToString() ?? "?"))})",
        ConstructorDeclarationSyntax constructor =>
            $".ctor({string.Join(",", constructor.ParameterList.Parameters.Select(parameter => parameter.Type?.ToString() ?? "?"))})",
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        _ => GetName(declaration)
    };

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record DeclarationEvidence(
        SyntaxNode Node,
        ISymbol? Symbol,
        SemanticModel? Model,
        string Identity,
        SourceLocation Location);

    internal sealed record BaseTypeEvidence(
        CodeEntity Source,
        BaseTypeSyntax BaseType,
        SemanticModel Model,
        string RelativePath);

    private sealed record SemanticEntityKey(string ProjectId, string SymbolIdentity);
}

internal sealed record SemanticExtraction(
    string ProjectId,
    string ProjectPath,
    string AssemblyIdentity,
    IReadOnlyList<CodeEntity> Entities,
    IReadOnlyList<CodeRelation> Relations,
    IReadOnlyList<string> Documents,
    IReadOnlyDictionary<ISymbol, CodeEntity> EntityBySymbol,
    IReadOnlyDictionary<string, CodeEntity> EntityByIdentity,
    IReadOnlyList<CSharpSymbolExtractor.BaseTypeEvidence> BaseTypes);

internal sealed record SyntaxExtraction(
    IReadOnlyList<CodeEntity> Entities,
    IReadOnlyList<CodeRelation> Relations,
    IReadOnlyList<string> Documents,
    int UnresolvedBaseCount);

internal sealed record TypeRelationResult(
    IReadOnlyList<CodeRelation> Relations,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics);
