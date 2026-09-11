using EngineeringBrain.Core;

namespace EngineeringBrain.Core.Tests;

public sealed class StableEntityIdTests
{
    [Fact]
    public void Create_NormalizesPathSeparators()
    {
        var first = StableEntityId.Create("C#", CodeEntityType.Class, "Src/Feature.cs", "Demo.Feature");
        var second = StableEntityId.Create("c#", CodeEntityType.Class, "Src\\Feature.cs", "Demo.Feature");

        Assert.Equal(first, second);
        Assert.StartsWith("entity:", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_PreservesCaseSensitivePathIdentity()
    {
        var upperCase = StableEntityId.Create("C#", CodeEntityType.Class, "Feature.cs", "Demo.Feature");
        var lowerCase = StableEntityId.Create("C#", CodeEntityType.Class, "feature.cs", "Demo.Feature");

        Assert.NotEqual(upperCase, lowerCase);
    }

    [Fact]
    public void Create_DistinguishesOverloadsByFullName()
    {
        var noArguments = StableEntityId.Create("C#", CodeEntityType.Method, "feature.cs", "Demo.Run()");
        var withArgument = StableEntityId.Create("C#", CodeEntityType.Method, "feature.cs", "Demo.Run(string)");

        Assert.NotEqual(noArguments, withArgument);
    }

    [Fact]
    public void CreateSemantic_IncludesProjectAndCanonicalSymbolIdentity()
    {
        var firstProject = StableEntityId.CreateSemantic(
            "C#",
            "project-a",
            CodeEntityType.Method,
            "M:Demo.Customer.Process(System.Int32)");
        var secondOverload = StableEntityId.CreateSemantic(
            "C#",
            "project-a",
            CodeEntityType.Method,
            "M:Demo.Customer.Process(System.String)");
        var secondProject = StableEntityId.CreateSemantic(
            "C#",
            "project-b",
            CodeEntityType.Method,
            "M:Demo.Customer.Process(System.Int32)");

        Assert.NotEqual(firstProject, secondOverload);
        Assert.NotEqual(firstProject, secondProject);
        Assert.Equal(
            firstProject,
            StableEntityId.CreateSemantic(
                "C#",
                "project-a",
                CodeEntityType.Method,
                "M:Demo.Customer.Process(System.Int32)"));
    }
}
