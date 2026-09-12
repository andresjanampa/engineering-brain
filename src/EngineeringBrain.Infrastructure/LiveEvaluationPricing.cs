using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed record LiveModelPricing(
    string Provider,
    string Model,
    decimal InputPerMillionTokens,
    decimal CachedInputPerMillionTokens,
    decimal OutputPerMillionTokens);

public sealed record LivePricingCatalog(int PricingSchemaVersion, IReadOnlyList<LiveModelPricing> Models)
{
    public decimal? Estimate(ReasoningCallUsage usage)
    {
        if (usage.ActualInputTokens is null || usage.ActualOutputTokens is null) return null;
        var price = Models.SingleOrDefault(value => value.Provider.Equals(usage.Provider, StringComparison.OrdinalIgnoreCase)
            && value.Model.Equals(usage.Model, StringComparison.Ordinal));
        if (price is null) return null;
        var cached = Math.Min(usage.CachedInputTokens ?? 0, usage.ActualInputTokens.Value);
        var uncached = usage.ActualInputTokens.Value - cached;
        return (uncached * price.InputPerMillionTokens
            + cached * price.CachedInputPerMillionTokens
            + usage.ActualOutputTokens.Value * price.OutputPerMillionTokens) / 1_000_000m;
    }
}

public static class LivePricingCatalogLoader
{
    public static async Task<LivePricingCatalog> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var catalog = JsonSerializer.Deserialize<LivePricingCatalog>(
            await File.ReadAllTextAsync(path, cancellationToken), options)
            ?? throw new InvalidDataException("Pricing configuration is empty.");
        if (catalog.PricingSchemaVersion != 1 || catalog.Models.Any(value => value.InputPerMillionTokens < 0
            || value.CachedInputPerMillionTokens < 0 || value.OutputPerMillionTokens < 0))
            throw new InvalidDataException("Pricing configuration is invalid.");
        return catalog;
    }
}
