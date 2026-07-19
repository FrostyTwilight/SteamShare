using Serilog;

using SteamShare.Core.Exceptions;

using Steamworks;

namespace SteamShare.Core.Services;

/// <summary>
/// Production implementation of <see cref="IWorkshopService"/> wrapping Steamworks.NET UGC API.
/// Depends on <see cref="SteamInitializer"/> for Steam client lifecycle and
/// <see cref="SteamCallbackDispatcher"/> for async callback bridging.
/// Uses <see cref="CallResultUtils.Wait{T}"/> for clean CallResult→Task bridging.
/// </summary>
public sealed class SteamWorkshopService : IWorkshopService
{
    private static readonly ILogger LogSerilog = Log.ForContext<SteamWorkshopService>();
    private readonly SteamInitializer _steamInitializer;
    private readonly SteamCallbackDispatcher _dispatcher;
    private readonly uint _appId;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, UGCUpdateHandle_t> _activeUploadHandles = new();

    public SteamWorkshopService(
        SteamInitializer steamInitializer,
        SteamCallbackDispatcher dispatcher,
        uint appId = SteamInitializer.DefaultAppId)
    {
        _steamInitializer = steamInitializer ?? throw new ArgumentNullException(nameof(steamInitializer));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _appId = appId;
    }

    /// <inheritdoc />
    public bool IsSteamRunning => _steamInitializer.IsInitialized;

    /// <inheritdoc />
    public ulong CurrentSteamId
    {
        get
        {
            if (_steamInitializer.IsInitialized)
            {
                return SteamUser.GetSteamID().m_SteamID;
            }

            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<bool> InitializeAsync()
    {
        LogSerilog.Information("Initializing SteamWorkshopService");
        if (!_steamInitializer.IsInitialized)
        {
            var result = _steamInitializer.Initialize();
            if (result != SteamInitResult.Success)
            {
                LogSerilog.Warning("SteamWorkshopService init failed: {Result}", result);
                return false;
            }
        }

        _dispatcher.Start();
        LogSerilog.Information("SteamWorkshopService initialized");

        var subCount = SteamUGC.GetNumSubscribedItems(true);
        var buff = new PublishedFileId_t[subCount];
        SteamUGC.GetSubscribedItems(buff, subCount, true);

        foreach (var v in buff)
        {
            var metadata = await QueryItemByIdAsync(v.m_PublishedFileId).ConfigureAwait(false);
            if (metadata != null)
            {
                LogSerilog.Debug("UnsubscribeItem: " + v);
                SteamUGC.UnsubscribeItem(v);
            }
        }

        return true;
    }

    /// <inheritdoc />
    public async Task ShutdownAsync()
    {
        if (_steamInitializer.IsInitialized)
        {
            await _dispatcher.StopAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<ulong> CreateItemAsync(WorkshopItemCreateRequest request,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default)
    {
        EnsureInitialized();

        LogSerilog.Debug("Creating workshop item: title={Title}", request.Title);

        var handle = SteamUGC.CreateItem(new AppId_t(_appId), EWorkshopFileType.k_EWorkshopFileTypeCommunity);
        var createResult = await handle.Wait<CreateItemResult_t>().ConfigureAwait(false);

        if (createResult.m_eResult != EResult.k_EResultOK)
        {
            LogSerilog.Error("CreateItem failed: {Result} for title={Title}", createResult.m_eResult, request.Title);
            throw new WorkshopUploadException($"CreateItem failed: {createResult.m_eResult}");
        }

        if (createResult.m_bUserNeedsToAcceptWorkshopLegalAgreement)
        {
            LogSerilog.Error("User must accept Steam Workshop legal agreement");
            throw new WorkshopUploadException("User must accept Steam Workshop legal agreement");
        }

        LogSerilog.Debug("Workshop item created: {PublishedFileId}", createResult.m_nPublishedFileId.m_PublishedFileId);

        await UpdateItemAsync(new WorkshopItemUpdateRequest
        {
            PublishedFileId = createResult.m_nPublishedFileId.m_PublishedFileId,
            Title = request.Title,
            Description = request.Description,
            MetadataJson = request.MetadataJson,
            ContentPath = request.ContentPath,
            Visibility = request.Visibility,
            KeyValueTags = request.KeyValueTags
        }, progressCallback, ct).ConfigureAwait(false);

        return createResult.m_nPublishedFileId.m_PublishedFileId;
    }

    /// <inheritdoc />
    public async Task UpdateItemAsync(WorkshopItemUpdateRequest request,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default)
    {
        EnsureInitialized();

        LogSerilog.Debug("Updating workshop item {PublishedFileId}", request.PublishedFileId);

        var updateHandle = SteamUGC.StartItemUpdate(new AppId_t(_appId), new PublishedFileId_t(request.PublishedFileId));
        var needsCommit = false;

        if (request.Title != null)
        {
            SteamUGC.SetItemTitle(updateHandle, request.Title);
            needsCommit = true;
        }

        if (request.Description != null)
        {
            SteamUGC.SetItemDescription(updateHandle, request.Description);
            needsCommit = true;
        }

        if (request.MetadataJson != null)
        {
            SteamUGC.SetItemMetadata(updateHandle, request.MetadataJson);
            needsCommit = true;
        }

        if (request.ContentPath != null)
        {
            SteamUGC.SetItemContent(updateHandle, request.ContentPath);
            needsCommit = true;
        }

        if (request.Visibility.HasValue)
        {
            var steamVisibility = request.Visibility.Value switch
            {
                WorkshopVisibility.Private => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
                WorkshopVisibility.Unlisted => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted,
                WorkshopVisibility.Public => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
                _ => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate
            };
            SteamUGC.SetItemVisibility(updateHandle, steamVisibility);
            needsCommit = true;
        }

        if (request.KeyValueTags != null && request.KeyValueTags.Count > 0)
        {
            foreach ((var key, var val) in request.KeyValueTags)
            {
                SteamUGC.AddItemKeyValueTag(updateHandle, key, val);
            }
            needsCommit = true;
        }

        if (needsCommit)
        {
            var apiCall = SteamUGC.SubmitItemUpdate(updateHandle, "");
            _activeUploadHandles[request.PublishedFileId] = updateHandle;
            var resultTask = apiCall.Wait<SubmitItemUpdateResult_t>();

            while (!resultTask.IsCompleted)
            {
                if (progressCallback != null)
                {
                    SteamUGC.GetItemUpdateProgress(updateHandle, out var bytesProcessed, out var bytesTotal);
                    progressCallback((long)bytesProcessed, (long)bytesTotal);
                }

                ct.ThrowIfCancellationRequested();
                await Task.Delay(1, ct).ConfigureAwait(false);
            }



            var result = await resultTask;
            _activeUploadHandles.TryRemove(request.PublishedFileId, out _);
            if (result.m_eResult != EResult.k_EResultOK)
            {
                LogSerilog.Error("SubmitItemUpdate failed: {Result} for {PublishedFileId}",
                    result.m_eResult, request.PublishedFileId);
                throw new WorkshopUploadException($"SubmitItemUpdate failed: {result.m_eResult}");
            }

            LogSerilog.Debug("Workshop item {PublishedFileId} updated successfully", request.PublishedFileId);
        }
    }

    /// <inheritdoc />
    public async Task<WorkshopItemDownloadResult> DownloadItemAsync(ulong publishedFileId,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default)
    {
        EnsureInitialized();

        var fileId = new PublishedFileId_t(publishedFileId);

        SteamUGC.SubscribeItem(fileId);
        if (!SteamUGC.DownloadItem(fileId, true))
        {
            throw new WorkshopDownloadException($"Failed to start download for item {publishedFileId}");
        }

        try
        {
            // Wait for steam ready
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while ((EItemState)SteamUGC.GetItemState(fileId) is EItemState.k_EItemStateNone or EItemState.k_EItemStateNeedsUpdate)
            {
                break;
            }

            ct.ThrowIfCancellationRequested();
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(1, cts.Token).ConfigureAwait(false);

        }
        catch (OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
        }

        SteamUGC.UnsubscribeItem(fileId);

        while (true)
        {
            var state = (EItemState)SteamUGC.GetItemState(fileId);

            if (state == (EItemState.k_EItemStateSubscribed | EItemState.k_EItemStateInstalled) ||
                state == EItemState.k_EItemStateInstalled)
            {
                break;
            }

            var progress = SteamUGC.GetItemDownloadInfo(fileId,
            out var bytesProcessed,
            out var bytesTotal);

            progressCallback?.Invoke((long)bytesProcessed, (long)bytesTotal);

            ct.ThrowIfCancellationRequested();

            await Task.Delay(1, ct).ConfigureAwait(false);
        }

        if (!SteamUGC.GetItemInstallInfo(fileId, out var sizeOnDisk, out var folder, 1024, out _))
        {
            throw new WorkshopDownloadException("Failed to get install info after download");
        }

        SteamUGC.UnsubscribeItem(fileId);

        return new WorkshopItemDownloadResult
        {
            PublishedFileId = publishedFileId,
            LocalFolderPath = folder,
            SizeOnDiskBytes = sizeOnDisk
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkshopItemInfo>> QueryOwnedItemsAsync(CancellationToken ct = default)
    {
        EnsureInitialized();

        LogSerilog.Debug("Querying owned items");
        var handle = SteamUGC.CreateQueryUserUGCRequest(
            SteamUser.GetSteamID().GetAccountID(),
            EUserUGCList.k_EUserUGCList_Published,
            EUGCMatchingUGCType.k_EUGCMatchingUGCType_All,
            EUserUGCListSortOrder.k_EUserUGCListSortOrder_CreationOrderDesc,
            new AppId_t(_appId), new AppId_t(_appId), 1);

        try
        {
            SteamUGC.SetReturnMetadata(handle, true);
            SteamUGC.SetReturnKeyValueTags(handle, true);
            SteamUGC.AddRequiredKeyValueTag(handle, "steamshare_tag", "true");

            var apiCall = SteamUGC.SendQueryUGCRequest(handle);
            var queryResult = await apiCall.Wait<SteamUGCQueryCompleted_t>().ConfigureAwait(false);

            if (queryResult.m_eResult != EResult.k_EResultOK)
            {
                LogSerilog.Error("Query UGC failed: {Result}", queryResult.m_eResult);
                throw new WorkshopDownloadException($"Query UGC failed: {queryResult.m_eResult}");
            }

            var items = new List<WorkshopItemInfo>();
            for (uint i = 0; i < queryResult.m_unNumResultsReturned; i++)
            {
                if (SteamUGC.GetQueryUGCResult(handle, i, out var details))
                {
                    SteamUGC.GetQueryUGCMetadata(handle, i, out var metadata, 4096);
                    SteamUGC.GetQueryUGCStatistic(handle, i, EItemStatistic.k_EItemStatistic_NumSubscriptions, out var subs);
                    SteamUGC.GetQueryUGCStatistic(handle, i, EItemStatistic.k_EItemStatistic_NumFavorites, out var favs);
                    items.Add(MapDetailsToItemInfo(details, metadata, (uint)subs, (uint)favs));
                }
            }

            LogSerilog.Debug("QueryOwnedItems returned {Count} items", items.Count);
            return items.AsReadOnly();
        }
        finally
        {
            SteamUGC.ReleaseQueryUGCRequest(handle);
        }
    }

    /// <inheritdoc />
    public async Task<WorkshopItemInfo?> QueryItemByIdAsync(ulong publishedFileId, CancellationToken ct = default)
    {
        EnsureInitialized();

        var fileIds = new[] { new PublishedFileId_t(publishedFileId) };
        var handle = SteamUGC.CreateQueryUGCDetailsRequest(fileIds, (uint)fileIds.Length);

        try
        {
            SteamUGC.SetReturnKeyValueTags(handle, true);
            SteamUGC.SetReturnMetadata(handle, true);

            var apiCall = SteamUGC.SendQueryUGCRequest(handle);
            var queryResult = await apiCall.Wait<SteamUGCQueryCompleted_t>().ConfigureAwait(false);

            if (queryResult.m_eResult != EResult.k_EResultOK)
            {
                throw new WorkshopDownloadException($"Query details failed: {queryResult.m_eResult}");
            }

            if (queryResult.m_unNumResultsReturned > 0
                && SteamUGC.GetQueryUGCResult(handle, 0, out var details))
            {
                SteamUGC.GetQueryUGCMetadata(handle, 0, out var metadata, 4096);
                SteamUGC.GetQueryUGCStatistic(handle, 0, EItemStatistic.k_EItemStatistic_NumSubscriptions, out var subs);
                SteamUGC.GetQueryUGCStatistic(handle, 0, EItemStatistic.k_EItemStatistic_NumFavorites, out var favs);
                return MapDetailsToItemInfo(details, metadata, (uint)subs, (uint)favs);
            }

            return null;
        }
        finally
        {
            SteamUGC.ReleaseQueryUGCRequest(handle);
        }
    }

    /// <inheritdoc />
    public async Task DeleteItemAsync(ulong publishedFileId, CancellationToken ct = default)
    {
        EnsureInitialized();

        LogSerilog.Information("Deleting workshop item {PublishedFileId}", publishedFileId);

        var result = await SteamUGC.DeleteItem(new PublishedFileId_t(publishedFileId))
            .Wait<DeleteItemResult_t>().ConfigureAwait(false);

        if (result.m_eResult != EResult.k_EResultOK)
        {
            LogSerilog.Error("Delete failed: {Result} for {PublishedFileId}", result.m_eResult, publishedFileId);
            throw new WorkshopUploadException($"Delete failed: {result.m_eResult}");
        }

        LogSerilog.Information("Workshop item {PublishedFileId} deleted", publishedFileId);
    }

    /// <inheritdoc />
    public Task SetItemVisibilityAsync(ulong publishedFileId, WorkshopVisibility visibility, CancellationToken ct = default)
    {
        return UpdateItemAsync(new WorkshopItemUpdateRequest
        {
            PublishedFileId = publishedFileId,
            Visibility = visibility
        }, null, ct);
    }

    /// <inheritdoc />
    public Task<WorkshopItemDownloadResult?> GetItemInstallInfoAsync(ulong publishedFileId)
    {
        EnsureInitialized();

        if (!SteamUGC.GetItemInstallInfo(new PublishedFileId_t(publishedFileId), out var sizeOnDisk, out var folder, 1024, out _))
        {
            return Task.FromResult<WorkshopItemDownloadResult?>(null);
        }

        return Task.FromResult<WorkshopItemDownloadResult?>(new WorkshopItemDownloadResult
        {
            PublishedFileId = publishedFileId,
            LocalFolderPath = folder,
            SizeOnDiskBytes = sizeOnDisk
        });
    }

    /// <inheritdoc />
    public Task<(ulong ProcessedBytes, ulong TotalBytes)> GetItemUploadProgressAsync(ulong publishedFileId)
    {
        if (_activeUploadHandles.TryGetValue(publishedFileId, out var handle))
        {
            SteamUGC.GetItemUpdateProgress(handle, out var processed, out var total);
            return Task.FromResult(((ulong)processed, (ulong)total));
        }

        return Task.FromResult((0UL, 0UL));
    }

    /// <inheritdoc />
    public Task<(ulong ProcessedBytes, ulong TotalBytes)> GetItemDownloadProgressAsync(ulong publishedFileId)
    {
        var fileId = new PublishedFileId_t(publishedFileId);
        if (SteamUGC.GetItemDownloadInfo(fileId, out var processed, out var total))
        {
            return Task.FromResult(((ulong)processed, (ulong)total));
        }

        return Task.FromResult((0UL, 0UL));
    }

    private void EnsureInitialized()
    {
        if (!_steamInitializer.IsInitialized)
        {
            throw new InvalidOperationException("SteamWorkshopService is not initialized. Call InitializeAsync() first.");
        }
    }

    private static WorkshopItemInfo MapDetailsToItemInfo(SteamUGCDetails_t details, string? metadata, uint subscriptions, uint favorites)
    {
        return new WorkshopItemInfo
        {
            PublishedFileId = details.m_nPublishedFileId.m_PublishedFileId,
            Title = details.m_rgchTitle,
            Description = details.m_rgchDescription,
            MetadataJson = metadata,
            Visibility = MapVisibility(details.m_eVisibility),
            OwnerSteamId = details.m_ulSteamIDOwner,
            Subscriptions = subscriptions,
            Favorites = favorites,
            DownloadCount = subscriptions,
        };
    }

    private static WorkshopVisibility MapVisibility(ERemoteStoragePublishedFileVisibility visibility)
    {
        return visibility switch
        {
            ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic => WorkshopVisibility.Public,
            ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly => WorkshopVisibility.Unlisted,
            _ => WorkshopVisibility.Private
        };
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }
}
