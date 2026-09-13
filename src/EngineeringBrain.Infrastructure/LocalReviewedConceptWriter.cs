using System.Diagnostics;
using System.Text;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class ReviewedConceptWriteConflictException : IOException
{
    public ReviewedConceptWriteConflictException(string message)
        : base(message)
    {
    }
}

public sealed class LocalReviewedConceptWriter
{
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultLockRetryDelay = TimeSpan.FromMilliseconds(25);

    private readonly LocalReviewedConceptStore _paths;
    private readonly TimeSpan _lockTimeout;
    private readonly TimeSpan _lockRetryDelay;

    public LocalReviewedConceptWriter(
        LocalReviewedConceptStore? paths = null,
        TimeSpan? lockTimeout = null,
        TimeSpan? lockRetryDelay = null)
    {
        _paths = paths ?? new LocalReviewedConceptStore();
        _lockTimeout = lockTimeout ?? DefaultLockTimeout;
        _lockRetryDelay = lockRetryDelay ?? DefaultLockRetryDelay;
        if (_lockTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockTimeout));
        }

        if (_lockRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockRetryDelay));
        }
    }

    public async Task<ReviewedConceptWriteResult> WriteAsync(
        string branchKnowledgeLocation,
        ReviewedConceptCatalog catalog,
        string? expectedCurrentFingerprint,
        Func<CancellationToken, Task> validateBeforeCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(validateBeforeCommit);
        cancellationToken.ThrowIfCancellationRequested();

        var targetPath = _paths.GetPath(branchKnowledgeLocation);
        var directory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(directory);
        var lockPath = targetPath + ".lock";
        await using var lockStream = await AcquireLockAsync(lockPath, cancellationToken);

        var initialFingerprint = await ReadFingerprintAsync(targetPath, cancellationToken);
        EnsureExpectedFingerprint(initialFingerprint, expectedCurrentFingerprint);

        var serialized = ReviewedConceptSerializer.Serialize(catalog);
        var newFingerprint = KnowledgeIdentity.ContentHash(serialized);
        if (string.Equals(initialFingerprint, newFingerprint, StringComparison.Ordinal))
        {
            return new ReviewedConceptWriteResult(
                ReviewedConceptWriteOutcome.Unchanged,
                targetPath,
                newFingerprint);
        }

        var tempPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(serialized);
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            var finalFingerprint = await ReadFingerprintAsync(targetPath, cancellationToken);
            EnsureExpectedFingerprint(finalFingerprint, expectedCurrentFingerprint);
            await validateBeforeCommit(cancellationToken);
            File.Move(tempPath, targetPath, overwrite: true);

            return new ReviewedConceptWriteResult(
                initialFingerprint is null
                    ? ReviewedConceptWriteOutcome.Created
                    : ReviewedConceptWriteOutcome.Updated,
                targetPath,
                newFingerprint);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private async Task<FileStream> AcquireLockAsync(
        string lockPath,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException) when (elapsed.Elapsed < _lockTimeout)
            {
                await Task.Delay(_lockRetryDelay, cancellationToken);
            }
            catch (IOException)
            {
                throw new ReviewedConceptWriteConflictException(
                    "Reviewed concept catalog is locked by another lifecycle operation.");
            }
        }
    }

    private static async Task<string?> ReadFingerprintAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return KnowledgeIdentity.ContentHash(content);
    }

    private static void EnsureExpectedFingerprint(
        string? actualFingerprint,
        string? expectedFingerprint)
    {
        if (!string.Equals(actualFingerprint, expectedFingerprint, StringComparison.Ordinal))
        {
            throw new ReviewedConceptWriteConflictException(
                "Reviewed concept catalog changed after its write precondition was established.");
        }
    }
}
