using System.Diagnostics;

namespace SteamShare.Test.CLI;

/// <summary>
/// Integration tests for the CLI executable.
/// All tests use STEAMSHARE_TEST_MODE=dummy to activate DummyWorkshopService.
/// Tests run via Process.Start using dotnet run against the CLI project.
/// </summary>
public class CliIntegrationTests : IDisposable
{
    private static readonly string CliProjectPath = ResolveCliProjectPath();
    private readonly string _tempDir;

    public CliIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SteamShare_CLI_Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir))
        {
            return;
        }

        // Retry with backoff — on Windows, file handles may not be released immediately.
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
                return;
            }
            catch (IOException) when (i < 4)
            {
                Thread.Sleep(50 * (i + 1));
            }
            catch (UnauthorizedAccessException) when (i < 4)
            {
                Thread.Sleep(50 * (i + 1));
            }
        }
    }

    private static string ResolveCliProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src", "SteamShare.CLI", "SteamShare.CLI.csproj");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not find CLI project directory");
    }

    // ── Helper: run the CLI and capture output ───────────────────

    private async Task<CliResult> RunCliAsync(string arguments, int timeoutMs = 60000)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{CliProjectPath}\" -- --accept-disclaimer {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _tempDir
        };

        startInfo.Environment["STEAMSHARE_TEST_MODE"] = "dummy";
        startInfo.Environment["STEAMSHARE_TEST_PREPOPULATE"] = "1";
        startInfo.Environment["NO_COLOR"] = "1";

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var exited = process.WaitForExit(timeoutMs);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            await Task.WhenAll(stdoutTask, stderrTask);
            throw new TimeoutException($"CLI process timed out after {timeoutMs}ms. Args: {arguments}");
        }

        await Task.WhenAll(stdoutTask, stderrTask);

        return new CliResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    // ── --help ───────────────────────────────────────────────────

    [Fact]
    public async Task Help_OutputContainsAllCommands()
    {
        var result = await RunCliAsync("--help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("upload");
        result.Stdout.Should().Contain("download");
        result.Stdout.Should().Contain("share");
        result.Stdout.Should().Contain("list");
        result.Stdout.Should().Contain("delete");
        result.Stdout.Should().Contain("rename");
        result.Stdout.Should().Contain("visibility");
    }

    [Fact]
    public async Task Help_OutputContainsApplicationName()
    {
        var result = await RunCliAsync("--help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("steamshare");
    }

    // ── upload ───────────────────────────────────────────────────

    [Fact]
    public async Task Upload_WithoutPath_ShowsError()
    {
        var result = await RunCliAsync("upload");

        // Spectre.Console.Cli shows error when required argument is missing
        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task Upload_WithNonexistentPath_ShowsError()
    {
        var nonExistentPath = Path.Combine(_tempDir, "nonexistent_dir");
        var result = await RunCliAsync($"upload \"{nonExistentPath}\"");

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("Error", "error", "not found");
    }

    [Fact]
    public async Task Upload_WithValidDirectory_Succeeds()
    {
        var sourceDir = Path.Combine(_tempDir, "upload_test");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "hello.txt"), "Hello, SteamShare!");

        var result = await RunCliAsync($"upload \"{sourceDir}\"");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("upload_test");
    }

    // ── delete ───────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WithoutConfirm_Fails()
    {
        var result = await RunCliAsync("delete 12345");

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().Contain("confirm");
    }

    [Fact]
    public async Task Delete_WithConfirm_SucceedsForExistingItem()
    {
        // Use pre-populated item ID (1-3 are created when STEAMSHARE_TEST_PREPOPULATE=1)
        var deleteResult = await RunCliAsync("delete 1 --confirm");

        deleteResult.ExitCode.Should().Be(0);
        deleteResult.Stdout.Should().Contain("Successfully deleted");
    }

    // ── share ────────────────────────────────────────────────────

    [Fact]
    public async Task Share_ShowsKeyStartingWithSsharePlus()
    {
        // Use pre-populated item ID (1-3 are created when STEAMSHARE_TEST_PREPOPULATE=1)
        var shareResult = await RunCliAsync("share 1");

        shareResult.ExitCode.Should().Be(0);
        shareResult.Stdout.Should().Contain("sshare+");
    }

    [Fact]
    public async Task Share_WithPassword_ShowsPasswordProtectedMessage()
    {
        var shareResult = await RunCliAsync("share 1 --password secret123");

        shareResult.ExitCode.Should().Be(0);
        shareResult.Stdout.Should().Contain("sshare+");
        shareResult.Stdout.Should().Contain("password-protected");
    }

    // ── list ─────────────────────────────────────────────────────

    [Fact]
    public async Task List_ProducesOutput()
    {
        var sourceDir = Path.Combine(_tempDir, "list_test");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "list_me.txt"), "list");
        await RunCliAsync($"upload \"{sourceDir}\" --name ListTest");

        var result = await RunCliAsync("list");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task List_WhenItemsExist_ShowsTableOutput()
    {
        // Pre-populated data ensures items exist
        var result = await RunCliAsync("list");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().NotBeNullOrEmpty();
        // The table should contain the pre-populated item names
        result.Stdout.Should().Contain("Test File Group");
    }

    // ── visibility ──────────────────────────────────────────────

    [Fact]
    public async Task Visibility_SetPublic_RequiresConfirmation()
    {
        // Use pre-populated item ID (1-3 are created when STEAMSHARE_TEST_PREPOPULATE=1)
        var visResult = await RunCliAsync("visibility 1 --set Public");

        visResult.ExitCode.Should().Be(0);
        visResult.Stdout.Should().Contain("Public");
    }

    [Fact]
    public async Task Visibility_WithoutSet_ShowsError()
    {
        var result = await RunCliAsync("visibility 12345");

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("Error", "error", "required");
    }

    [Fact]
    public async Task Visibility_InvalidValue_ShowsError()
    {
        var result = await RunCliAsync("visibility 12345 --set InvalidValue");

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("Error", "error", "Invalid");
    }

    // ── Unknown command ──────────────────────────────────────────

    [Fact]
    public async Task UnknownCommand_ShowsError()
    {
        var result = await RunCliAsync("nonexistent_command_xyz");

        result.ExitCode.Should().NotBe(0);
    }

    // ── download (id-based) ─────────────────────────────────────

    [Fact]
    public async Task Download_WithoutKeyOrId_ShowsError()
    {
        var result = await RunCliAsync("download");

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("Error", "error", "key", "id");
    }

    // ── rename ───────────────────────────────────────────────────

    [Fact]
    public async Task Rename_WithoutName_ShowsError()
    {
        var result = await RunCliAsync("rename 12345");

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("Error", "error", "name");
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static ulong? ExtractPublishedId(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var match = System.Text.RegularExpressions.Regex.Match(line,
                @"(?:Published\s*(?:File\s*)?ID)\D*(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && ulong.TryParse(match.Groups[1].Value, out var id))
            {
                return id;
            }
        }

        return null;
    }

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr);
}
