using EngineeringBrain.Core;

namespace EngineeringBrain.Analyzers.CSharp.Tests;

public sealed class CSharpSemanticAnalysisTests
{
    [Fact]
    public async Task AnalyzeAsync_ModelsLanguageConstructsAndSemanticIdentity()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSdkProject("Domain/Domain.csproj");
        repository.Write("Domain/Traditional.cs", """
            namespace Traditional.Namespace
            {
                public class Container
                {
                    public class Nested
                    {
                    }
                }
            }
            """);
        repository.Write("Domain/Modern.Part1.cs", """
            namespace Modern;

            public interface IService<T> { }
            public interface IAudit { }
            public class Base<T> { }

            public partial class Worker<T> : Base<T>, IService<T>, IAudit
            {
                public void Process(int id) { }
            }

            public record WorkItem(int Id);
            public enum WorkState { Pending }
            """);
        repository.Write("Domain/Modern.Part2.cs", """
            namespace Modern;

            public partial class Worker<T>
            {
                public string Name { get; } = string.Empty;
                public void Process(string id) { }
            }
            """);
        repository.Write("Domain/Duplicates.cs", """
            namespace First { public class Duplicate { } }
            namespace Second { public class Duplicate { } }
            """);

        var result = await repository.AnalyzeAsync();

        Assert.Equal(AnalysisMode.FullSemantic, result.Analysis.Mode);
        Assert.Contains(result.Entities, entity => entity is
        { EntityType: CodeEntityType.Namespace, FullName: "Traditional.Namespace" });
        Assert.Contains(result.Entities, entity => entity is
        { EntityType: CodeEntityType.Namespace, FullName: "Modern" });
        Assert.Contains(result.Entities, entity => entity.EntityType == CodeEntityType.Interface);
        Assert.Contains(result.Entities, entity => entity.EntityType == CodeEntityType.Record);
        Assert.Contains(result.Entities, entity => entity.EntityType == CodeEntityType.Enum);
        Assert.Contains(result.Entities, entity => entity is
        { EntityType: CodeEntityType.Class, FullName: "Traditional.Namespace.Container.Nested" });

        var worker = Assert.Single(result.Entities, entity => entity is
        { EntityType: CodeEntityType.Class, Name: "Worker" });
        Assert.Equal(ResolutionLevel.Semantic, worker.ResolutionLevel);
        Assert.Single(worker.AdditionalLocations);
        Assert.Contains("Worker<T>", worker.FullName, StringComparison.Ordinal);

        var overloads = result.Entities
            .Where(entity => entity is { EntityType: CodeEntityType.Method, Name: "Process" })
            .ToArray();
        Assert.Equal(2, overloads.Length);
        Assert.Equal(2, overloads.Select(entity => entity.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(overloads, entity => entity.FullName.Contains("int", StringComparison.Ordinal));
        Assert.Contains(overloads, entity => entity.FullName.Contains("string", StringComparison.Ordinal));

        var duplicateClasses = result.Entities
            .Where(entity => entity is { EntityType: CodeEntityType.Class, Name: "Duplicate" })
            .ToArray();
        Assert.Equal(2, duplicateClasses.Length);
        Assert.Equal(2, duplicateClasses.Select(entity => entity.Id).Distinct(StringComparer.Ordinal).Count());

        var workerRelations = result.Relations
            .Where(relation => relation.SourceEntityId == worker.Id)
            .ToArray();
        Assert.Single(workerRelations, relation => relation.RelationType == CodeRelationType.Inherits);
        Assert.Equal(2, workerRelations.Count(relation => relation.RelationType == CodeRelationType.Implements));
        Assert.All(
            workerRelations.Where(relation => relation.RelationType is CodeRelationType.Inherits or CodeRelationType.Implements),
            relation => Assert.Equal(ResolutionLevel.Semantic, relation.ResolutionLevel));
    }
}
