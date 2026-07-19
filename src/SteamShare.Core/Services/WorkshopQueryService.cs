using Serilog;

using SteamShare.Core.Models;

namespace SteamShare.Core.Services;

/// <summary>
/// Higher-level query service that wraps <see cref="IWorkshopService"/>
/// and maps workshop items to <see cref="FileGroup"/> domain entities.
/// Filters items by the <c>steamshare_tag=true</c> key-value tag.
/// </summary>
public class WorkshopQueryService
{
    private static readonly ILogger LogSerilog = Log.ForContext<WorkshopQueryService>();
    private const string TagKey = "steamshare_tag";
    private const string TagValue = "true";

    private readonly IWorkshopService _workshop;

    public WorkshopQueryService(IWorkshopService workshop)
    {
        _workshop = workshop;
    }

    /// <summary>
    /// Queries all owned workshop items tagged with <c>steamshare_tag=true</c>
    /// and maps them to <see cref="FileGroup"/> entities.
    /// </summary>
    public async Task<IReadOnlyList<FileGroup>> QueryOwnedFileGroupsAsync(CancellationToken ct = default)
    {
        LogSerilog.Debug("Querying owned file groups");
        var items = await _workshop.QueryOwnedItemsAsync(ct);
        var result = items
            .Select(item => TryMapToFileGroup(item))
            .OfType<FileGroup>()
            .ToList()
            .AsReadOnly();
        LogSerilog.Debug("Found {Count} owned file groups", result.Count);
        return result;
    }

    /// <summary>
    /// Queries a single workshop item by ID, returning a <see cref="FileGroup"/>
    /// if the item exists and is tagged with <c>steamshare_tag=true</c>.
    /// Returns <c>null</c> otherwise.
    /// </summary>
    public async Task<FileGroup?> QueryFileGroupByIdAsync(ulong publishedFileId, CancellationToken ct = default)
    {
        var item = await _workshop.QueryItemByIdAsync(publishedFileId, ct);
        if (item is null || !HasSteamShareTag(item))
        {
            return null;
        }

        return TryMapToFileGroup(item);
    }

    private static bool HasSteamShareTag(WorkshopItemInfo item)
    {
        return item.KeyValueTags is not null
            && item.KeyValueTags.TryGetValue(TagKey, out var value)
            && value == TagValue;
    }

    private static FileGroup? TryMapToFileGroup(WorkshopItemInfo item)
    {
        if (string.IsNullOrEmpty(item.MetadataJson))
        {
            return null;
        }

        try
        {
            var metadata = FileGroupMetadata.FromJson(item.MetadataJson);
            return FileGroup.FromMetadata(
                metadata,
                item.PublishedFileId,
                item.OwnerSteamId,
                item.Visibility,
                downloadCount: item.DownloadCount);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
