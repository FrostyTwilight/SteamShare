using Serilog;

using SteamShare.Core.Exceptions;
using SteamShare.Core.Models;

namespace SteamShare.Core.Services;

/// <summary>
/// Orchestrates share key generation and resolution.
/// Coordinates between IShareKeyCryptoService and IWorkshopService.
/// </summary>
public sealed class ShareService
{
    private static readonly ILogger LogSerilog = Log.ForContext<ShareService>();
    private readonly IShareKeyCryptoService _crypto;
    private readonly IWorkshopService _workshop;

    public ShareService(IShareKeyCryptoService crypto, IWorkshopService workshop)
    {
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _workshop = workshop ?? throw new ArgumentNullException(nameof(workshop));
    }

    /// <summary>
    /// Generate a share key for a workshop item.
    /// If the item is Private, changes visibility to Unlisted.
    /// </summary>
    public async Task<string> GenerateShareKeyAsync(ulong publishedFileId, string? password = null, CancellationToken ct = default)
    {
        LogSerilog.Information("Generating share key for item {PublishedFileId}", publishedFileId);

        var item = await _workshop.QueryItemByIdAsync(publishedFileId, ct).ConfigureAwait(false);
        if (item == null)
        {
            LogSerilog.Error("Share key generation failed: item {PublishedFileId} not found", publishedFileId);
            throw new FileGroupNotFoundException(publishedFileId);
        }

        // If private, change to Unlisted so the key can be used
        if (item.Visibility == WorkshopVisibility.Private)
        {
            LogSerilog.Debug("Changing visibility from Private to Unlisted for {PublishedFileId}", publishedFileId);
            await _workshop.SetItemVisibilityAsync(publishedFileId, WorkshopVisibility.Unlisted, ct).ConfigureAwait(false);
        }

        var key = _crypto.GenerateShareKey(publishedFileId, password);
        LogSerilog.Information("Share key generated for item {PublishedFileId}", publishedFileId);
        return key;
    }

    /// <summary>
    /// Resolve a share key to a workshop item's metadata.
    /// Throws if the key is invalid or the item cannot be accessed.
    /// </summary>
    public async Task<FileGroupMetadata> ResolveShareKeyAsync(string shareKey, string? password = null, CancellationToken ct = default)
    {
        LogSerilog.Information("Resolving share key");

        var payload = _crypto.ParseShareKey(shareKey, password);
        LogSerilog.Debug("Share key resolved to item {PublishedFileId}", payload.Id);

        var item = await _workshop.QueryItemByIdAsync(payload.Id, ct);
        if (item == null)
        {
            LogSerilog.Error("Share key resolution failed: item {PublishedFileId} not found", payload.Id);
            throw new FileGroupNotFoundException(payload.Id);
        }

        if (item.MetadataJson == null)
        {
            LogSerilog.Error("Share key resolution failed: item {PublishedFileId} has no metadata", payload.Id);
            throw new ShareKeyParseException("Workshop item has no metadata");
        }
        return FileGroupMetadata.FromJson(item.MetadataJson);
    }
}
