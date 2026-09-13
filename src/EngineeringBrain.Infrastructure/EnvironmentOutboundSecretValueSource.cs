using System.Collections;
using System.Text.RegularExpressions;

namespace EngineeringBrain.Infrastructure;

public interface IOutboundSecretValueSource
{
    IReadOnlySet<string> GetValues();
}

public sealed partial class EnvironmentOutboundSecretValueSource : IOutboundSecretValueSource
{
    public const int MinimumSecretLength = 8;

    private static readonly HashSet<string> ExcludedValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "yes", "no", "on", "off", "none", "null",
        "development", "production", "staging", "test", "local", "localhost",
        "changeme", "placeholder", "example", "dummy"
    };

    private readonly HashSet<string> _values;

    public EnvironmentOutboundSecretValueSource()
        : this(ReadEnvironment())
    {
    }

    public EnvironmentOutboundSecretValueSource(IEnumerable<KeyValuePair<string, string?>> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        _values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            if (!IsSecretName(variable.Key) || string.IsNullOrWhiteSpace(variable.Value))
            {
                continue;
            }

            var value = variable.Value.Trim();
            if (value.Length >= MinimumSecretLength && !ExcludedValues.Contains(value))
            {
                _values.Add(value);
            }
        }
    }

    public IReadOnlySet<string> GetValues() => new HashSet<string>(_values, StringComparer.Ordinal);

    private static bool IsSecretName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalized = name.Replace('-', '_').ToUpperInvariant();
        return SecretName().IsMatch(normalized);
    }

    private static IEnumerable<KeyValuePair<string, string?>> ReadEnvironment() =>
        Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Select(entry => new KeyValuePair<string, string?>(
                entry.Key?.ToString() ?? string.Empty,
                entry.Value?.ToString()));

    [GeneratedRegex(
        @"(?:^|_)(?:SECRET|TOKEN|PASSWORD|PASSWD|API_KEY|APIKEY|ACCESS_KEY|PRIVATE_KEY|CONNECTION_STRING)(?:_|$)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretName();
}
