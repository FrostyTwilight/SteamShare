using System.ComponentModel;

using Serilog;

using Spectre.Console;
using Spectre.Console.Cli;

using SteamShare.Core.Exceptions;
using SteamShare.Core.Services;

namespace SteamShare.CLI.Commands;

/// <summary>
/// Settings for <see cref="RenameCommand"/>.
/// </summary>
public sealed class RenameSettings : CommandSettings
{
    /// <summary>
    /// Published file ID to rename.
    /// </summary>
    [CommandArgument(0, "<id>")]
    [Description("Published file ID to rename.")]
    public ulong PublishedFileId { get; init; }

    /// <summary>
    /// New name for the file group.
    /// </summary>
    [CommandOption("--name")]
    [Description("New name for the file group.")]
    public string? Name { get; init; }
}

/// <summary>
/// Renames a published file group.
/// Updates the workshop item title (hashed) and metadata name.
/// </summary>
public sealed class RenameCommand : AsyncCommand<RenameSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, RenameSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Name))
        {
            AnsiConsole.MarkupLine(
                "[red]Error:[/] A new name is required. Pass [yellow]--name \"New Name\"[/].");
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
                        $"[green]Renaming file group {settings.PublishedFileId}[/]",
                        autoStart: true);
                    task.IsIndeterminate = true;

                    await fileGroupManager.RenameAsync(settings.PublishedFileId, settings.Name);

                    task.Description = $"[green]Renamed to '{settings.Name}'[/]";
                    task.StopTask();
                });

            AnsiConsole.MarkupLine(
                $"[green]Successfully renamed file group [yellow]{settings.PublishedFileId}[/] " +
                $"to [yellow]'{settings.Name}'[/].[/]");

            return 0;
        }
        catch (FileGroupNotFoundException ex)
        {
            Log.Error(ex, "Rename failed — not found: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Not found:[/] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Rename error: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}
