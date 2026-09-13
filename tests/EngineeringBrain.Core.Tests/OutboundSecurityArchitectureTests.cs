using System.Reflection;
using System.Text.RegularExpressions;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class OutboundSecurityArchitectureTests
{
    [Fact]
    public void Architecture_ProviderContractHasNoRawReasoningRequestParameter()
    {
        Assert.DoesNotContain(typeof(IReasoningProvider).GetMethods(), method =>
            method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ReasoningRequest)));
    }

    [Fact]
    public void Architecture_ApprovedRequestHasNoPublicCreationPath()
    {
        var type = typeof(ApprovedReasoningRequest);

        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.Static), method => method.ReturnType == type);
    }

    [Fact]
    public void Architecture_AllProductionProviderCallsUseApprovedRequest()
    {
        var root = FindRepositoryRoot();
        var productionFiles = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories);
        var constructionSites = productionFiles
            .Where(path => File.ReadAllText(path).Contains("new ApprovedReasoningRequest", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToArray();

        Assert.Equal(["src/EngineeringBrain.Infrastructure/OutboundRequestGate.cs"], constructionSites);
        Assert.DoesNotContain(productionFiles, path => Regex.IsMatch(
            File.ReadAllText(path),
            @"GenerateStructuredAsync\s*<[^>]+>\s*\(\s*ReasoningRequest\s+request\b",
            RegexOptions.CultureInvariant));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "EngineeringBrain.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("EngineeringBrain.sln was not found.");
    }
}
