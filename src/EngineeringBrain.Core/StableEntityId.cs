using System.Security.Cryptography;
using System.Text;

namespace EngineeringBrain.Core;

public static class StableEntityId
{
    public static string Create(
        string language,
        CodeEntityType entityType,
        string relativeFilePath,
        string fullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        var canonicalPath = relativeFilePath.Replace('\\', '/').TrimStart('/');
        return Hash($"file-v1|{language.ToLowerInvariant()}|{entityType}|{canonicalPath}|{fullName}");
    }

    public static string CreateSemantic(
        string language,
        string projectId,
        CodeEntityType entityType,
        string symbolIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolIdentity);

        return Hash($"symbol-v2|{language.ToLowerInvariant()}|{projectId}|{entityType}|{symbolIdentity}");
    }

    private static string Hash(string identity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));

        return $"entity:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
