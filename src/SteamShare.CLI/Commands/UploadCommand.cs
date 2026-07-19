using System.ComponentModel;

using Serilog;

using Spectre.Console;
using Spectre.Console.Cli;

using SteamShare.Core.Exceptions;
using SteamShare.Core.Services;
using SteamShare.Core.Utilities;

namespace SteamShare.CLI.Commands;

/// <summary>
/// Settings for <see cref="UploadCommand"/>.
/// </summary>
public sealed class UploadSettings : CommandSettings
{
    /// <summary>
    /// Path to the directory to upload.
    /// </summary>
    [CommandArgument(0, "<path>")]
    [Description("Path to the directory to upload.")]
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Custom name for the file group. Defaults to the directory name.
    /// </summary>
    [CommandOption("--name")]
    [Description("Custom name for the file group. Defaults to directory name.")]
    public string? Name { get; init; }

    /// <summary>
    /// Workshop visibility (Private, Unlisted, Public).
    /// </summary>
    [CommandOption("--visibility")]
    [Description("Workshop visibility: Private, Unlisted, or Public.")]
    public string Visibility { get; init; } = "Private";
}

/// <summary>
/// Uploads a directory as a ZIP file group to Steam Workshop.
/// </summary>
public sealed class UploadCommand : AsyncCommand<UploadSettings>
{
    private const string DefaultVisibility = "Private";

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, UploadSettings settings, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(settings.Path))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Directory not found: [yellow]{settings.Path}[/]");
            return 1;
        }

        var visibility = ParseVisibility(settings.Visibility);
        if (visibility is null)
        {
            AnsiConsole.MarkupLine(
                $"[red]Error:[/] Invalid visibility [yellow]'{settings.Visibility}'[/]. " +
                "Use: Private, Unlisted, or Public.");
            return 1;
        }

        var name = settings.Name ?? new DirectoryInfo(settings.Path).Name;
        var fileGroupManager = AppServices.Get<FileGroupManager>();

        try
        {
            return await AnsiConsole.Progress()
                .Columns(
                    new SpinnerColumn(),
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn())
                .StartAsync(async ctx =>
                {
                    var zipTask = ctx.AddTask("[green]Creating ZIP archive[/]", autoStart: true);
                    zipTask.IsIndeterminate = true;

                    var fileGroup = await fileGroupManager.CreateFromDirectoryAsync(
                        settings.Path, name);

                    zipTask.Description = "[green]ZIP archive created[/]";
                    zipTask.Value = zipTask.MaxValue;
                    zipTask.StopTask();

                    var uploadTask = ctx.AddTask("[yellow]Uploading to Steam Workshop[/]", autoStart: true);
                    uploadTask.IsIndeterminate = true;

                    var result = await fileGroupManager.UploadAsync(fileGroup, visibility.Value,
                        (progress, total) =>
                        {
                            uploadTask.IsIndeterminate = false;
                            uploadTask.MaxValue = total;
                            uploadTask.Value = progress;
                        }, cancellationToken);

                    uploadTask.Description = "[yellow]Upload completed[/]";
                    uploadTask.Value = uploadTask.MaxValue;
                    uploadTask.StopTask();

                    var ownerStr = result.OwnerSteamId != 0
                        ? result.OwnerSteamId.ToString()
                        : "unknown";

                    AnsiConsole.WriteLine();
                    var table = new Table()
                        .AddColumn("Property")
                        .AddColumn("Value")
                        .AddRow("Name", result.Name)
                        .AddRow("Published ID", result.PublishedFileId.ToString())
                        .AddRow("Files", result.FileCount.ToString())
                        .AddRow("Size", FileSizeFormatter.Format(result.TotalSizeBytes))
                        .AddRow("Visibility", result.Visibility.ToString())
                        .AddRow("Owner", ownerStr)
                        .AddRow("SHA-256", result.ManifestHash ?? "N/A");

                    AnsiConsole.Write(table);
                    return 0;
                });
        }
        catch (WorkshopUploadException ex)
        {
            Log.Error(ex, "Upload failed: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Upload failed:[/] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Upload error: {Message}", ex.Message);
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
