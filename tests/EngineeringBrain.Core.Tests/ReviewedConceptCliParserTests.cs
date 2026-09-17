namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptCliParserTests
{
    public static TheoryData<string[]> MalformedRemapArguments => new()
    {
        new[] { "concepts", "remap", "--map", "old", "new" },
        new[] { "concepts", "remap", "--reviewer", "alice", "--map", "old" },
        new[]
        {
            "concepts", "remap", "--reviewer", "alice",
            "--map", "old", "new", "--map", "old", "other"
        },
        new[] { "concepts", "remap", "--reviewer", "alice", "--map", "old", "new", "--unknown" },
        new[]
        {
            "concepts", "remap", "--reviewer", "alice", "--map", "old", "new",
            "--repo", ".", "--repo", "."
        },
        new[] { "concepts", "remap", "unexpected", "--reviewer", "alice", "--map", "old", "new" },
        new[] { "concepts", "remap", "--reviewer", "alice", "--map", "--old", "new" },
        new[] { "concepts", "remap", "--reviewer", "alice", "--map", "old", "--new" },
        new[]
        {
            "concepts", "remap", "--reviewer", "alice", "--map", "old", "new", "--repo", "--path"
        }
    };

    [Theory]
    [MemberData(nameof(MalformedRemapArguments))]
    public async Task RunAsync_MalformedConceptRemapReturnsUsageExitCode(string[] args)
    {
        var exitCode = await BrainCli.RunAsync(args);

        Assert.Equal(2, exitCode);
    }
}
