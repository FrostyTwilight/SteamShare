using System.ComponentModel;

using Serilog;

using Spectre.Console;
using Spectre.Console.Cli;

using SteamShare.Core.Exceptions;
using SteamShare.Core.Services;

namespace SteamShare.CLI.Commands;

/// <summary>
/// Settings for <see cref="VisibilityCommand"/>.
/// </summary>
public sealed class VisibilitySettings : CommandSettings
{
    /// <summary>
    /// Published file ID to change visibility for.
    /// </summary>
    [CommandArgument(0, "<id>")]
    [Description("Published file ID to change visibility for.")]
    public ulong PublishedFileId { get; init; }

    /// <summary>
    /// Target visibility value (Private, Unlisted, Public).
    /// </summary>
    [CommandOption("--set")]
    [Description("Target visibility: Private, Unlisted, or Public.")]
    public string? Set { get; init; }
}

/// <summary>
/// Changes the visibility of a published file group.
/// Public visibility requires explicit confirmation.
/// </summary>
public sealed class VisibilityCommand : AsyncCommand<VisibilitySettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, VisibilitySettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.Set))
        {
            AnsiConsole.MarkupLine(
                "[red]Error:[/] Target visibility is required. " +
                "Pass [yellow]--set Private[/], [yellow]--set Unlisted[/], or [yellow]--set Public[/].");
            return 1;
        }

        var visibility = ParseVisibility(settings.Set);
        if (visibility is null)
        {
            AnsiConsole.MarkupLine(
                $"[red]Error:[/] Invalid visibility [yellow]'{settings.Set}'[/]. " +
                "Use: Private, Unlisted, or Public.");
            return 1;
        }

        try
        {
            var visibilityService = AppServices.Get<VisibilityService>();

            await AnsiConsole.Progress()
                .Columns(
                    new SpinnerColumn(),
                    new TaskDescriptionColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask(
                        $"[green]Changing visibility of {settings.PublishedFileId} to {visibility}[/]",
                        autoStart: true);
                    task.IsIndeterminate = true;

                    await visibilityService.ChangeVisibilityAsync(
                        settings.PublishedFileId, visibility.Value, confirmed: true);

                    task.Description =
                        $"[green]Visibility changed to {visibility}[/]";
                    task.StopTask();
                });

            var label = visibility.Value switch
            {
                WorkshopVisibility.Public => "[red]Public[/]",
                WorkshopVisibility.Unlisted => "[yellow]Unlisted[/]",
                _ => "[grey]Private[/]"
            };

            AnsiConsole.MarkupLine(
                $"[green]Visibility of [yellow]{settings.PublishedFileId}[/] " +
                $"changed to {label}.[/]");

            return 0;
        }
        catch (FileGroupNotFoundException ex)
        {
            Log.Error(ex, "Visibility change failed — not found: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Not found:[/] {ex.Message}");
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            Log.Error(ex, "Visibility change error: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Visibility change error: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private static WorkshopVisibility? ParseVisibility(string visibility)
    {
        return visibility switch
        {
            "Private" => WorkshopVisibility.Private,
            "Unlisted" => WorkshopVisibility.Unlisted,
            "Public" => WorkshopVisibility.Public,
            _ => null
        };
    }
}
