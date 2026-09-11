namespace EngineeringBrain.Core;

public enum ScanExecutionMode
{
    Full,
    Incremental
}

public enum FileChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed
}

public enum ChangeDetectionMethod
{
    ContentHash,
    FileMetadata,
    FileSystem,
    Git
}

public enum SnapshotLoadStatus
{
    Loaded,
    NotFound,
    Incompatible,
    Corrupt
}
