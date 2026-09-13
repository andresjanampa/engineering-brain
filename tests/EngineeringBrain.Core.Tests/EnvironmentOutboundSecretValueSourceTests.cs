using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class EnvironmentOutboundSecretValueSourceTests
{
    private const string SecretValue = "real-looking-secret-value";

    [Theory]
    [InlineData("APP_SECRET")]
    [InlineData("ACCESS_TOKEN")]
    [InlineData("DB_PASSWORD")]
    [InlineData("SERVICE_PASSWD")]
    [InlineData("OPENAI_API_KEY")]
    [InlineData("LEGACY_APIKEY")]
    [InlineData("AWS_ACCESS_KEY")]
    [InlineData("SIGNING_PRIVATE_KEY")]
    [InlineData("DB_CONNECTION_STRING")]
    public void GetValues_IncludesHighConfidenceSecretNames(string name)
    {
        var source = Source((name, SecretValue));

        Assert.Contains(SecretValue, source.GetValues());
    }

    [Theory]
    [InlineData("PATH", "C:\\tools")]
    [InlineData("TOKENIZER_MODE", "something-long")]
    [InlineData("APP_SECRET", "short")]
    [InlineData("APP_SECRET", "development")]
    [InlineData("APP_SECRET", "placeholder")]
    [InlineData("APP_SECRET", null)]
    [InlineData("APP_SECRET", "")]
    [InlineData("APP_SECRET", "   ")]
    public void GetValues_ExcludesUnsafeFalsePositiveInputs(string name, string? value)
    {
        Assert.Empty(Source((name, value)).GetValues());
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("none")]
    [InlineData("null")]
    [InlineData("production")]
    [InlineData("staging")]
    [InlineData("test")]
    [InlineData("local")]
    [InlineData("localhost")]
    [InlineData("changeme")]
    [InlineData("example")]
    [InlineData("dummy")]
    public void GetValues_ExcludesFrozenCommonValuesCaseInsensitively(string value)
    {
        Assert.Empty(Source(("APP_SECRET", value.ToUpperInvariant())).GetValues());
    }

    [Fact]
    public void GetValues_NormalizesHyphenatedNamesAndTrimsValues()
    {
        var source = Source(("app-secret", $"  {SecretValue}  "));

        Assert.Equal([SecretValue], source.GetValues());
    }

    [Fact]
    public void GetValues_DeduplicatesOrdinallyAndReturnsDefensiveCopies()
    {
        var source = Source(
            ("FIRST_SECRET", SecretValue),
            ("SECOND_TOKEN", SecretValue),
            ("THIRD_PASSWORD", SecretValue.ToUpperInvariant()));

        var first = Assert.IsType<HashSet<string>>(source.GetValues());
        Assert.Equal(2, first.Count);
        first.Clear();

        Assert.Equal(2, source.GetValues().Count);
    }

    [Fact]
    public void ToString_DoesNotExposeSelectedValue()
    {
        var source = Source(("APP_SECRET", SecretValue));

        Assert.DoesNotContain(SecretValue, source.ToString(), StringComparison.Ordinal);
    }

    private static EnvironmentOutboundSecretValueSource Source(params (string Name, string? Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string?>(value.Name, value.Value)));
}
