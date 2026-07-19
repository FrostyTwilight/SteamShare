using System.Text.Json.Serialization;

using SteamShare.Core.Services;

namespace SteamShare.Core.Models;

/// <summary>
/// Represents a file group — a folder of files published to Steam Workshop.
/// This is the primary domain entity.
/// </summary>
public sealed record FileGroup
{
    /// <summary>Steam Workshop published file ID. 0 if not yet published.</summary>
    public ulong PublishedFileId { get; init; }

    /// <summary>Human-readable name of the file group.</summary>
    public required string Name { get; init; }

    /// <summary>Virtual folder path within the file group tree. Empty string for root.</summary>
    public string VirtualFolderPath { get; init; } = string.Empty;

    /// <summary>Local path to the downloaded file group folder on disk. Null if not downloaded.</summary>
    [JsonIgnore]
    public string? LocalFolderPath { get; init; }

    /// <summary>
    /// SHA-256 hash of the filegroup_sha.json manifest, hex-encoded.
    /// Null if not yet computed.
    /// </summary>
    public string? ManifestHash { get; init; }

    /// <summary>Number of files contained in the file group.</summary>
    public int FileCount { get; init; }

    /// <summary>Total uncompressed size of all files in bytes.</summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>UTC timestamp when this file group was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC timestamp of last update. Null if never updated.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Steam 64-bit ID of the owner. 0 if unknown.</summary>
    public ulong OwnerSteamId { get; init; }

    /// <summary>Current workshop visibility.</summary>
    public WorkshopVisibility Visibility { get; init; } = WorkshopVisibility.Private;

    /// <summary>Number of times this file group has been downloaded (subscriptions).</summary>
    public uint DownloadCount { get; init; }

    // ── Obsolete backward-compat properties ──────────────────────────

    /// <summary>
    /// [Obsolete] Local path to the downloaded file group folder on disk.
    /// Use <see cref="LocalFolderPath"/> instead.
    /// </summary>
    [Obsolete("Use LocalFolderPath instead")]
    [JsonIgnore]
    public string? LocalZipPath
    {
        get => LocalFolderPath;
        init => LocalFolderPath = value;
    }

    /// <summary>
    /// [Obsolete] SHA-256 hash of the file group, hex-encoded.
    /// Use <see cref="ManifestHash"/> instead.
    /// </summary>
    [Obsolete("Use ManifestHash instead")]
    public string? Sha256Hash
    {
        get => ManifestHash;
        init => ManifestHash = value;
    }

    // ── Methods ──────────────────────────────────────────────────────

    /// <summary>
    /// Converts this FileGroup to a lightweight metadata record
    /// suitable for storage in Steam Workshop item metadata (size-limited).
    /// </summary>
    public FileGroupMetadata ToMetadata() => new()
    {
        Name = Name,
        VirtualFolderPath = VirtualFolderPath,
        ManifestHash = ManifestHash,
        FileCount = FileCount,
        TotalSizeBytes = TotalSizeBytes,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };

    /// <summary>
    /// Creates a FileGroup from workshop metadata, filling in
    /// additional fields from workshop query results.
    /// </summary>
    public static FileGroup FromMetadata(FileGroupMetadata metadata, ulong publishedFileId,
        ulong ownerSteamId, WorkshopVisibility visibility, string? localFolderPath = null,
        uint downloadCount = 0) => new()
        {
            PublishedFileId = publishedFileId,
            Name = metadata.Name,
            VirtualFolderPath = metadata.VirtualFolderPath ?? string.Empty,
            LocalFolderPath = localFolderPath,
            ManifestHash = metadata.ManifestHash,
            FileCount = metadata.FileCount,
            TotalSizeBytes = metadata.TotalSizeBytes,
            CreatedAt = metadata.CreatedAt,
            UpdatedAt = metadata.UpdatedAt,
            OwnerSteamId = ownerSteamId,
            Visibility = visibility,
            DownloadCount = downloadCount
        };
}

/// <summary>
/// Lightweight metadata stored in the Steam Workshop item's metadata field.
/// Size-constrained (~5KB limit on Steam Workshop metadata).
/// New optional fields (Version, GitCommit) are short strings.
/// </summary>
public sealed record FileGroupMetadata
{
    public required string Name { get; init; }
    public string? VirtualFolderPath { get; init; }

    /// <summary>SHA-256 hash of the filegroup_sha.json manifest, hex-encoded.
    /// Serialized as "sha256_hash" in JSON for backward compatibility.</summary>
    [JsonPropertyName("sha256_hash")]
    public string? ManifestHash { get; init; }

    public int FileCount { get; init; }
    public long TotalSizeBytes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Current SteamShare version string (e.g. "0.1.0").</summary>
    public string? Version { get; init; }

    /// <summary>SteamShare git commit hash at time of upload.</summary>
    public string? GitCommit { get; init; }

    /// <summary>
    /// [Obsolete] SHA-256 hash, hex-encoded. Use <see cref="ManifestHash"/> instead.
    /// </summary>
    [Obsolete("Use ManifestHash instead")]
    public string? Sha256Hash
    {
        get => ManifestHash;
        init => ManifestHash = value;
    }

    /// <summary>
    /// Serializes this metadata to a JSON string for storage in
    /// the Steam Workshop item's metadata field.
    /// </summary>
    public string ToJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }

    /// <summary>
    /// Deserializes a JSON string back into a <see cref="FileGroupMetadata"/> instance.
    /// Old data using "sha256_hash" key maps to <see cref="ManifestHash"/> automatically.
    /// </summary>
    public static FileGroupMetadata FromJson(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<FileGroupMetadata>(json)
            ?? throw new FormatException("Invalid file group metadata JSON");
    }
}
