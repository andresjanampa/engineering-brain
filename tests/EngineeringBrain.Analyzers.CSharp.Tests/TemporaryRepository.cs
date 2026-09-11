using System.Text;
using EngineeringBrain.Analyzers.CSharp;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Analyzers.CSharp.Tests;

internal sealed class TemporaryRepository : IDisposable
{
    public TemporaryRepository()
    {
        Root = Path.Combine(Path.GetTempPath(), $"engineering-brain-csharp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Write(string relativePath, string contents)
    {
        var fullPath = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
    }

    public void Delete(string relativePath)
    {
        var fullPath = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(fullPath);
    }

    public void WriteSdkProject(string relativePath, string? projectReference = null)
    {
        var reference = projectReference is null
            ? string.Empty
            : $"<ItemGroup><ProjectReference Include=\"{projectReference}\" /></ItemGroup>";
        Write(relativePath, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>latest</LangVersion>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              {{reference}}
            </Project>
            """);
    }

    public void WriteSolution(string relativePath, params (string Name, string ProjectPath, string Id)[] projects)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        builder.AppendLine("# Visual Studio Version 17");
        foreach (var project in projects)
        {
            builder.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{project.Name}\", \"{project.ProjectPath}\", \"{{{project.Id}}}\"");
            builder.AppendLine("EndProject");
        }

        builder.AppendLine("Global");
        builder.AppendLine("EndGlobal");
        Write(relativePath, builder.ToString());
    }

    public async Task<LanguageAnalysisResult> AnalyzeAsync()
    {
        var scan = await new RepositoryScanner().ScanAsync(Root);
        return await new CSharpAnalyzer().AnalyzeAsync(new LanguageAnalysisRequest(Root, scan.Files));
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
