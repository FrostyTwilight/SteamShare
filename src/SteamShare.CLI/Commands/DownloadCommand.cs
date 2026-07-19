using System.ComponentModel;

using Serilog;

using Spectre.Console;
using Spectre.Console.Cli;

using SteamShare.Core.Exceptions;
using SteamShare.Core.Services;
using SteamShare.Core.Utilities;

namespace SteamShare.CLI.Commands;

/// <summary>
/// Settings for <see cref="DownloadCommand"/>.
/// </summary>
public sealed class DownloadSettings : CommandSettings
{
    [CommandOption("--password")]
    [Description("The password of share key.")]
    public string? Password { get; init; }
    /// <summary>
    /// Share key string to download (sshare+...).
    /// </summary>
    [CommandOption("--key")]
    [Description("Share key string to download (sshare+...).")]
    public string? ShareKey { get; init; }

    /// <summary>
    /// Published file ID to download directly.
    /// </summary>
    [CommandOption("--id")]
    [Description("Published file ID to download directly.")]
    public ulong? PublishedFileId { get; init; }

    /// <summary>
    /// Output directory for the downloaded file.
    /// Defaults to the OS downloads folder.
    /// </summary>
    [CommandOption("-o|--output")]
    [Description("Output directory for the downloaded file.")]
    public string? OutputDir { get; init; }
}

/// <summary>
/// Downloads a file group via share key or direct published file ID.
/// </summary>
public sealed class DownloadCommand : AsyncCommand<DownloadSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, DownloadSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.ShareKey) && settings.PublishedFileId is null)
        {
            AnsiConsole.MarkupLine(
                "[red]Error:[/] Either [yellow]--key[/] or [yellow]--id[/] must be specified.");
            return 1;
        }

        var outputDir = settings.OutputDir;

        try
        {
            ulong publishedFileId;

            if (!string.IsNullOrEmpty(settings.ShareKey))
            {
                // Resolve share key
                var shareService = AppServices.Get<ShareService>();

                var resolveTask = AnsiConsole.Progress()
                    .Columns(
                        new SpinnerColumn(),
                        new TaskDescriptionColumn())
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask("[green]Resolving share key[/]", autoStart: true);
                        task.IsIndeterminate = true;

                        var metadata = await shareService.ResolveShareKeyAsync(settings.ShareKey, settings.Password);

                        task.Description = "[green]Share key resolved[/]";
                        task.StopTask();
                        return metadata;
                    });

                var metadata = await resolveTask;
                publishedFileId = 0; // Need the actual ID from ShareKey payload

                // Re-extract the ID from the share key properly
                var cryptoService = AppServices.Get<IShareKeyCryptoService>();
                var payload = cryptoService.ParseShareKey(settings.ShareKey, settings.Password);
                publishedFileId = payload.Id;

                AnsiConsole.MarkupLine(
                    $"[green]Resolved:[/] [yellow]{metadata.Name}[/] (ID: {publishedFileId})");
            }
            else
            {
                publishedFileId = settings.PublishedFileId!.Value;
            }

            var fileGroupManager = AppServices.Get<FileGroupManager>();

            return await AnsiConsole.Progress()
                .Columns(
                    new SpinnerColumn(),
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[yellow]Downloading from Steam Workshop[/]", autoStart: true);
                    task.IsIndeterminate = true;

                    var result = await fileGroupManager.DownloadAsync(publishedFileId,
                        outputDir
                        );

                    task.Description = "[yellow]Download completed[/]";
                    task.Value = task.MaxValue;
                    task.StopTask();

                    AnsiConsole.WriteLine();
                    var table = new Table()
                        .AddColumn("Property")
                        .AddColumn("Value")
                        .AddRow("Name", result.Name)
                        .AddRow("Published ID", result.PublishedFileId.ToString())
                        .AddRow("Files", result.FileCount.ToString())
                        .AddRow("Size", FileSizeFormatter.Format(result.TotalSizeBytes))
                        .AddRow("Saved to", result.LocalFolderPath ?? "N/A")
                        .AddRow("SHA-256", result.ManifestHash ?? "N/A");

                    AnsiConsole.Write(table);
                    return 0;
                });
        }
        catch (ShareKeyParseException ex)
        {
            Log.Error(ex, "Share key parse failed: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Invalid share key:[/] {ex.Message}");
            return 1;
        }
        catch (FileGroupNotFoundException ex)
        {
            Log.Error(ex, "File group not found: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Not found:[/] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download error: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            throw;
        }
    }
}
