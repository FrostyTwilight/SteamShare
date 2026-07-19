using Serilog;

namespace SteamShare.Core.Services;

/// <summary>
/// Validates workshop item visibility transitions.
/// Risky transitions (to <see cref="WorkshopVisibility.Public"/>) require explicit confirmation.
/// </summary>
public class VisibilityService
{
    private static readonly ILogger LogSerilog = Log.ForContext<VisibilityService>();
    private readonly FileGroupManager _fileGroupManager;

    /// <summary>
    /// Initializes a new instance of <see cref="VisibilityService"/>.
    /// </summary>
    /// <param name="fileGroupManager">The file group manager for workshop operations.</param>
    public VisibilityService(FileGroupManager fileGroupManager)
    {
        _fileGroupManager = fileGroupManager ?? throw new ArgumentNullException(nameof(fileGroupManager));
    }

    /// <summary>
    /// Changes the visibility of a published workshop item.
    /// Transitions to <see cref="WorkshopVisibility.Public"/> require confirmation
    /// to prevent accidental public exposure.
    /// </summary>
    /// <param name="id">Published file ID of the workshop item.</param>
    /// <param name="to">Target workshop visibility.</param>
    /// <param name="confirmed">
    /// Must be <c>true</c> when transitioning to <see cref="WorkshopVisibility.Public"/>.
    /// </param>
    /// <param name="ct">CancellationToken token.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when transitioning to Public without confirmation.
    /// </exception>
    public async Task ChangeVisibilityAsync(ulong id, WorkshopVisibility to, bool confirmed, CancellationToken ct = default)
    {
        var metadata = await _fileGroupManager.GetMetadataAsync(id, ct);
        LogSerilog.Information("Changing visibility of {PublishedFileId} to {Visibility} (confirmed={Confirmed})",
            id, to, confirmed);

        if (to == WorkshopVisibility.Public && !confirmed)
        {
            LogSerilog.Warning("Visibility change to Public for {PublishedFileId} rejected: not confirmed", id);
            throw new InvalidOperationException("Transition to Public visibility requires confirmation.");
        }

        await _fileGroupManager.SetVisibilityAsync(id, to, ct);
        LogSerilog.Information("Visibility of {PublishedFileId} changed to {Visibility}", id, to);
    }

    /// <summary>
    /// Returns a localized warning message key for the visibility transition.
    /// Only risky transitions (to Public) produce a warning key; safe transitions return an empty string.
    /// </summary>
    /// <param name="from">Current workshop visibility.</param>
    /// <param name="to">Target workshop visibility.</param>
    /// <returns>A warning message key, or an empty string if no warning is needed.</returns>
    public string GetVisibilityWarning(WorkshopVisibility from, WorkshopVisibility to)
    {
        if (to == WorkshopVisibility.Public && from != WorkshopVisibility.Public)
        {
            return $"VisibilityWarning.{from}To{to}";
        }

        return string.Empty;
    }
}
