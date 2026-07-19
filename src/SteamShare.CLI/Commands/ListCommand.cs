using Serilog;

using Spectre.Console;
using Spectre.Console.Cli;

using SteamShare.Core.Models;
using SteamShare.Core.Services;

namespace SteamShare.CLI.Commands;

/// <summary>
/// Settings for <see cref="ListCommand"/>.
/// </summary>
public sealed class ListSettings : CommandSettings
{
}

/// <summary>
/// Lists all owned file groups in a Spectre.Console <see cref="Table"/>.
/// </summary>
public sealed class ListCommand : AsyncCommand<ListSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ListSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var queryService = AppServices.Get<WorkshopQueryService>();
            var trackingDb = AppServices.Get<TrackingDatabaseService>();

            IReadOnlyList<FileGroup> fileGroups = [];

            await AnsiConsole.Progress()
                .Columns(
                    new SpinnerColumn(),
                    new TaskDescriptionColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[green]Querying owned file groups[/]", autoStart: true);
                    task.IsIndeterminate = true;

                    fileGroups = await queryService.QueryOwnedFileGroupsAsync();

                    task.Description = $"[green]Found {fileGroups.Count} file group(s)[/]";
                    task.StopTask();
                });

            if (fileGroups.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No file groups found. Use [blue]upload[/] to create one.[/]");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("[bold]ID[/]")
                .AddColumn("[bold]Name[/]")
                .AddColumn("[bold]Visibility[/]")
                .AddColumn("[bold]Subscribers[/]")
                .AddColumn("[bold]Local[/]");

            foreach (var fg in fileGroups)
            {
                var isLocal = trackingDb.GetByPublishedFileId(fg.PublishedFileId) is { State: DownloadState.Downloaded }
                    ? "[green]Yes[/]"
                    : "[grey]No[/]";

                var visibilityColor = fg.Visibility switch
                {
                    WorkshopVisibility.Public => "[red]Public[/]",
                    WorkshopVisibility.Unlisted => "[yellow]Unlisted[/]",
                    _ => "[grey]Private[/]"
                };

                table.AddRow(
                    fg.PublishedFileId.ToString(),
                    fg.Name,
                    visibilityColor,
                    "—",
                    isLocal);
            }

            AnsiConsole.Write(table);

            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "List query error: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}
