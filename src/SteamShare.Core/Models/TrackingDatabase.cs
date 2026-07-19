using SteamShare.Core.Services;

namespace SteamShare.Core.Models;

/// <summary>
/// State of a file group download or upload operation.
/// </summary>
public enum DownloadState
{
    NotDownloaded,
    Downloading,
    Downloaded,
    Uploading,
    PendingDownload,
    PendingUpload
}

/// <summary>
/// Entry in the local tracking database for a file group.
/// Persisted as filegroups.json in the user data directory.
/// </summary>
public sealed record FileGroupTrackingEntry
{
    /// <summary>Steam Workshop published file ID. 0 if not yet published.</summary>
    public ulong PublishedFileId { get; init; }

    /// <summary>Local path to the downloaded ZIP. Null if not downloaded.</summary>
    public string? LocalPath { get; init; }

    /// <summary>Current download/upload state.</summary>
    public DownloadState State { get; init; } = DownloadState.NotDownloaded;

    /// <summary>Cached file group name for offline display.</summary>
    public string CachedName { get; init; } = string.Empty;

    /// <summary>Cached workshop visibility.</summary>
    public WorkshopVisibility CachedVisibility { get; init; } = WorkshopVisibility.Private;

    /// <summary>Last time this entry was synced with the workshop.</summary>
    public DateTimeOffset? LastSyncedAt { get; init; }
}

/// <summary>
/// Full tracking database, persisted as filegroups.json.
/// Thread-safe for read operations; writes should be serialized.
/// </summary>
public sealed record TrackingDatabase
{
    public IReadOnlyList<FileGroupTrackingEntry> Entries { get; init; } = Array.Empty<FileGroupTrackingEntry>();
}
