using System.ComponentModel;

using Serilog;

using Spectre.Console;
using Spectre.Console.Cli;

using SteamShare.Core.Exceptions;
using SteamShare.Core.Services;

namespace SteamShare.CLI.Commands;

/// <summary>
/// Settings for <see cref="ShareCommand"/>.
/// </summary>
public sealed class ShareSettings : CommandSettings
{
    /// <summary>
    /// Published file ID to generate a share key for.
    /// </summary>
    [CommandArgument(0, "<id>")]
    [Description("Published file ID to generate a share key for.")]
    public ulong PublishedFileId { get; init; }

    /// <summary>
    /// Optional password to encrypt the share key.
    /// </summary>
    [CommandOption("--password")]
    [Description("Optional password to encrypt the share key.")]
    public string? Password { get; init; }
}

/// <summary>
/// Generates a share key for a published file group.
/// </summary>
public sealed class ShareCommand : AsyncCommand<ShareSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ShareSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var shareService = AppServices.Get<ShareService>();

            var shareKey = await AnsiConsole.Progress()
                .Columns(
                    new SpinnerColumn(),
                    new TaskDescriptionColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[green]Generating share key[/]", autoStart: true);
                    task.IsIndeterminate = true;

                    var key = await shareService.GenerateShareKeyAsync(
                        settings.PublishedFileId,
                        settings.Password);

                    task.Description = "[green]Share key generated[/]";
                    task.StopTask();
                    return key;
                });

            AnsiConsole.WriteLine();
            var hasPassword = !string.IsNullOrEmpty(settings.Password);

            var panel = new Panel(shareKey)
            {
                Header = new PanelHeader("[bold green]Share Key[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 2, 1, 2)
            };

            AnsiConsole.Write(panel);

            if (hasPassword)
            {
                AnsiConsole.MarkupLine(
                    "[yellow]This share key is password-protected.[/] " +
                    "The recipient will need the password to download.");
            }

            return 0;
        }
        catch (FileGroupNotFoundException ex)
        {
            Log.Error(ex, "Share key generation failed — not found: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Not found:[/] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Share key generation error: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}
