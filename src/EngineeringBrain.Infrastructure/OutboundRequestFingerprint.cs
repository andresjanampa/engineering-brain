using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public static class OutboundRequestFingerprint
{
    private const string FormatIdentifier = "engineering-brain/reasoning-request/v1";

    public static string Create(ReasoningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, FormatIdentifier);
        AppendInt32(hash, (int)request.Stage);
        AppendString(hash, request.Model);
        AppendString(hash, request.SystemInstructions);
        AppendString(hash, request.UserData);
        AppendInt32(hash, request.MaximumOutputTokens);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
