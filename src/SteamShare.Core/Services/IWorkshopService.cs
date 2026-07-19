namespace SteamShare.Core.Services;

/// <summary>
/// Defines the visibility level of a Steam Workshop item.
/// Determines who can discover and access the item.
/// </summary>
public enum WorkshopVisibility
{
    /// <summary>
    /// Item is visible only to its owner.
    /// </summary>
    Private = 0,

    /// <summary>
    /// Item is accessible via a share key but does not appear
    /// in public Workshop listings or search results.
    /// </summary>
    Unlisted = 1,

    /// <summary>
    /// Item is visible to all Steam users and appears
    /// in public Workshop listings.
    /// </summary>
    Public = 2
}

/// <summary>
/// Request payload for creating a new Steam Workshop item.
/// Specifies the content, metadata, tags, and visibility
/// of the item to be published.
/// </summary>
public record WorkshopItemCreateRequest
{
    /// <summary>
    /// The display title of the Workshop item.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// A human-readable description of the Workshop item.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Application-specific metadata stored as a JSON string
    /// in the Workshop item's key-value tags.
    /// </summary>
    public required string MetadataJson { get; init; }

    /// <summary>
    /// Path to the local folder whose contents will be uploaded
    /// as the Workshop item payload. Pass <c>null</c> for
    /// metadata-only items with no file content.
    /// </summary>
    public required string? ContentPath { get; init; }

    /// <summary>
    /// The initial visibility of the created item.
    /// Defaults to <see cref="WorkshopVisibility.Private"/>.
    /// </summary>
    public WorkshopVisibility Visibility { get; init; } = WorkshopVisibility.Private;

    /// <summary>
    /// Optional Steam Workshop key-value tags to attach to the item.
    /// Used by SteamShare for internal identification and metadata storage.
    /// </summary>
    public IReadOnlyDictionary<string, string>? KeyValueTags { get; init; }
}

/// <summary>
/// Request payload for updating an existing Steam Workshop item.
/// Only the properties that are set to a non-<c>null</c> value
/// will be changed; all others retain their current values.
/// </summary>
public record WorkshopItemUpdateRequest
{
    /// <summary>
    /// The published file ID of the Workshop item to update.
    /// </summary>
    public required ulong PublishedFileId { get; init; }

    /// <summary>
    /// New display title. Pass <c>null</c> to keep the current title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// New description text. Pass <c>null</c> to keep the current description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// New metadata JSON string. Pass <c>null</c> to keep the current metadata.
    /// </summary>
    public string? MetadataJson { get; init; }

    /// <summary>
    /// New content folder path. Pass <c>null</c> to keep the current content.
    /// </summary>
    public string? ContentPath { get; init; }

    /// <summary>
    /// New visibility setting. Pass <c>null</c> to keep the current visibility.
    /// </summary>
    public WorkshopVisibility? Visibility { get; init; }

    /// <summary>
    /// New key-value tags. Pass <c>null</c> to keep the current tags.
    /// </summary>
    public IReadOnlyDictionary<string, string>? KeyValueTags { get; init; }
}

/// <summary>
/// Result of a completed Workshop item download.
/// Contains the local path and size of the downloaded content.
/// </summary>
public record WorkshopItemDownloadResult
{
    /// <summary>
    /// The published file ID of the downloaded Workshop item.
    /// </summary>
    public required ulong PublishedFileId { get; init; }

    /// <summary>
    /// The absolute path to the local folder where the item's
    /// files were placed after download.
    /// </summary>
    public required string LocalFolderPath { get; init; }

    /// <summary>
    /// Total size of the downloaded content on disk, in bytes.
    /// </summary>
    public required ulong SizeOnDiskBytes { get; init; }
}

/// <summary>
/// Information about a Steam Workshop item, including its
/// metadata, visibility, and community statistics.
/// </summary>
public record WorkshopItemInfo
{
    /// <summary>
    /// The published file ID that uniquely identifies this Workshop item.
    /// </summary>
    public required ulong PublishedFileId { get; init; }

    /// <summary>
    /// The display title of the Workshop item.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The description text of the Workshop item, or <c>null</c>
    /// if not available.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Application-specific metadata JSON stored with the item,
    /// or <c>null</c> if not available.
    /// </summary>
    public string? MetadataJson { get; init; }

    /// <summary>
    /// The current visibility setting of the Workshop item.
    /// </summary>
    public WorkshopVisibility Visibility { get; init; }

    /// <summary>
    /// The Steam ID of the user who owns this Workshop item.
    /// </summary>
    public ulong OwnerSteamId { get; init; }

    /// <summary>
    /// Number of Steam users currently subscribed to this item.
    /// </summary>
    public uint Subscriptions { get; init; }

    /// <summary>
    /// Number of times this item has been marked as a favorite.
    /// </summary>
    public uint Favorites { get; init; }

    /// <summary>
    /// Total number of times this item has been downloaded.
    /// </summary>
    public uint DownloadCount { get; init; }

    /// <summary>
    /// Steam Workshop key-value tags attached to this item,
    /// or <c>null</c> if none are present.
    /// </summary>
    public IReadOnlyDictionary<string, string>? KeyValueTags { get; init; }
}

/// <summary>
/// Provides operations for managing Steam Workshop items under the
/// Spacewar sandbox (App ID 480), including creating, updating,
/// downloading, querying, and deleting items. Also exposes Steam
/// client connection status.
/// </summary>
/// <remarks>
/// All async operations require the Steam client to be running
/// and logged in. Call <see cref="InitializeAsync"/> before using
/// any other methods, and <see cref="ShutdownAsync"/> when done.
/// </remarks>
public interface IWorkshopService
{
    /// <summary>
    /// Initializes the Steam Workshop API connection and prepares
    /// the service for use. Must be called once before any other
    /// Workshop operations.
    /// </summary>
    /// <returns>
    /// <c>true</c> if initialization succeeded and the Steam client
    /// is ready; <c>false</c> if the Steam client is not running
    /// or the user is not logged in.
    /// </returns>
    Task<bool> InitializeAsync();

    /// <summary>
    /// Shuts down the Steam Workshop API and releases all resources.
    /// Call once when the application is closing to perform
    /// orderly cleanup.
    /// </summary>
    Task ShutdownAsync();

    /// <summary>
    /// Creates a new Steam Workshop item with the specified content,
    /// metadata, and visibility. The item is published under the
    /// Spacewar sandbox (App ID 480).
    /// </summary>
    /// <param name="request">The creation request with title, description,
    /// metadata, content path, visibility, and optional tags.</param>
    /// <param name="progressCallback">
    /// Optional callback invoked during upload with
    /// <c>(processedBytes, totalBytes)</c> for progress reporting.
    /// </param>
    /// <param name="ct">Cancellation token to cancel the creation.</param>
    /// <returns>The published file ID of the newly created Workshop item.</returns>
    Task<ulong> CreateItemAsync(WorkshopItemCreateRequest request,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing Steam Workshop item. Only non-<c>null</c>
    /// properties in the request are changed; all other fields
    /// retain their current values.
    /// </summary>
    /// <param name="request">The update request specifying the item ID
    /// and which fields to change.</param>
    /// <param name="progressCallback">
    /// Optional callback invoked during content upload with
    /// <c>(processedBytes, totalBytes)</c> for progress reporting.
    /// </param>
    /// <param name="ct">Cancellation token to cancel the update.</param>
    Task UpdateItemAsync(WorkshopItemUpdateRequest request,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default);

    /// <summary>
    /// Downloads the content of a Steam Workshop item to local storage.
    /// The files are placed in a temporary Steam directory before being
    /// moved to their final destination.
    /// </summary>
    /// <param name="publishedFileId">The published file ID of the item to download.</param>
    /// <param name="progressCallback">
    /// Optional callback invoked during download with
    /// <c>(processedBytes, totalBytes)</c> for progress reporting.
    /// </param>
    /// <param name="ct">Cancellation token to cancel the download.</param>
    /// <returns>
    /// A <see cref="WorkshopItemDownloadResult"/> with the local folder path
    /// and size on disk.
    /// </returns>
    Task<WorkshopItemDownloadResult> DownloadItemAsync(ulong publishedFileId,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default);

    /// <summary>
    /// Queries all Steam Workshop items owned by the currently
    /// logged-in Steam user.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the query.</param>
    /// <returns>
    /// A read-only list of <see cref="WorkshopItemInfo"/> for every
    /// Workshop item owned by the current user.
    /// </returns>
    Task<IReadOnlyList<WorkshopItemInfo>> QueryOwnedItemsAsync(CancellationToken ct = default);

    /// <summary>
    /// Queries a single Steam Workshop item by its published file ID.
    /// </summary>
    /// <param name="publishedFileId">The published file ID to look up.</param>
    /// <param name="ct">Cancellation token to cancel the query.</param>
    /// <returns>
    /// A <see cref="WorkshopItemInfo"/> describing the item,
    /// or <c>null</c> if the item was not found.
    /// </returns>
    Task<WorkshopItemInfo?> QueryItemByIdAsync(ulong publishedFileId, CancellationToken ct = default);

    /// <summary>
    /// Deletes a Steam Workshop item permanently.
    /// This operation cannot be undone.
    /// </summary>
    /// <param name="publishedFileId">The published file ID of the item to delete.</param>
    /// <param name="ct">Cancellation token to cancel the deletion.</param>
    Task DeleteItemAsync(ulong publishedFileId, CancellationToken ct = default);

    /// <summary>
    /// Changes the visibility of an existing Steam Workshop item.
    /// </summary>
    /// <param name="publishedFileId">The published file ID of the item.</param>
    /// <param name="visibility">The new <see cref="WorkshopVisibility"/> to apply.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    Task SetItemVisibilityAsync(ulong publishedFileId, WorkshopVisibility visibility, CancellationToken ct = default);

    /// <summary>
    /// Retrieves local installation information for a subscribed
    /// Workshop item, including the folder path and size on disk.
    /// </summary>
    /// <param name="publishedFileId">The published file ID to query.</param>
    /// <returns>
    /// A <see cref="WorkshopItemDownloadResult"/> with install path and size,
    /// or <c>null</c> if the item is not installed locally.
    /// </returns>
    Task<WorkshopItemDownloadResult?> GetItemInstallInfoAsync(ulong publishedFileId);

    /// <summary>
    /// Queries the current upload progress for a Workshop item
    /// that is being created or updated.
    /// </summary>
    /// <param name="publishedFileId">The published file ID of the uploading item.</param>
    /// <returns>
    /// A tuple of <c>(processedBytes, totalBytes)</c> representing
    /// bytes uploaded so far and total bytes to upload.
    /// </returns>
    Task<(ulong ProcessedBytes, ulong TotalBytes)> GetItemUploadProgressAsync(ulong publishedFileId);

    /// <summary>
    /// Queries the current download progress for a Workshop item
    /// that is being downloaded.
    /// </summary>
    /// <param name="publishedFileId">The published file ID of the downloading item.</param>
    /// <returns>
    /// A tuple of <c>(processedBytes, totalBytes)</c> representing
    /// bytes downloaded so far and total bytes to download.
    /// </returns>
    Task<(ulong ProcessedBytes, ulong TotalBytes)> GetItemDownloadProgressAsync(ulong publishedFileId);

    /// <summary>
    /// Indicates whether the Steam client is currently running
    /// and accessible.
    /// </summary>
    bool IsSteamRunning { get; }

    /// <summary>
    /// The Steam ID of the currently logged-in user.
    /// Returns <c>0</c> if no user is logged in.
    /// </summary>
    ulong CurrentSteamId { get; }
}
