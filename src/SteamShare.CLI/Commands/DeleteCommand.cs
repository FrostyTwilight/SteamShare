using System.ComponentModel;

using Serilog;

using Spectre.Console;
using Spectre.Console.Cli;

using SteamShare.Core.Exceptions;
using SteamShare.Core.Services;

namespace SteamShare.CLI.Commands;

/// <summary>
/// Settings for <see cref="DeleteCommand"/>.
/// </summary>
public sealed class DeleteSettings : CommandSettings
{
    /// <summary>
    /// Published file ID to delete.
    /// </summary>
    [CommandArgument(0, "<id>")]
    [Description("Published file ID to delete.")]
    public ulong PublishedFileId { get; init; }

    /// <summary>
    /// Confirmation flag required to proceed with deletion.
    /// </summary>
    [CommandOption("--confirm")]
    [Description("Confirm deletion. Required to proceed.")]
    public bool Confirm { get; init; }
}

/// <summary>
/// Deletes a file group from Steam Workshop.
/// Requires <c>--confirm</c> to proceed.
/// </summary>
public sealed class DeleteCommand : AsyncCommand<DeleteSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, DeleteSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.Confirm)
        {
            AnsiConsole.MarkupLine(
                "[red]Error:[/] Deletion requires confirmation. " +
                "Pass [yellow]--confirm[/] to proceed.");
            return 1;
        }

        try
        {
            var fileGroupManager = AppServices.Get<FileGroupManager>();

            await AnsiConsole.Progress()
                .Columns(
                    new SpinnerColumn(),
                    new TaskDescriptionColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask(
                        $"[red]Deleting file group {settings.PublishedFileId}[/]",
                        autoStart: true);
                    task.IsIndeterminate = true;

                    await fileGroupManager.DeleteAsync(settings.PublishedFileId);

                    task.Description = $"[red]Deleted file group {settings.PublishedFileId}[/]";
                    task.StopTask();
                });

            AnsiConsole.MarkupLine(
                $"[green]Successfully deleted file group [yellow]{settings.PublishedFileId}[/].[/]");

            return 0;
        }
        catch (FileGroupNotFoundException ex)
        {
            Log.Error(ex, "Delete failed — not found: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Not found:[/] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Delete error: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}
