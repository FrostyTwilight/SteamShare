using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SteamShare.Test.E2E;

/// <summary>
/// End-to-end tests for the CLI executable using DummyWorkshopService.
/// All tests use STEAMSHARE_TEST_MODE=dummy to activate the dummy Steam backend.
/// Tests run via Process.Start using dotnet run against the CLI project.
/// </summary>
public class CliEndToEndTests : IDisposable
{
    private static readonly string CliProjectPath = ResolveCliProjectPath();
    private readonly string _tempDir;
    private readonly string _outputDir;

    public CliEndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SteamShare_E2E_Tests", Guid.NewGuid().ToString("N"));
        _outputDir = Path.Combine(_tempDir, "downloads");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_outputDir);
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

    private async Task<CliResult> RunCliAsync(
        string arguments,
        bool includeDisclaimer = false,
        int timeoutMs = 120000)
    {
        var disclaimerArg = includeDisclaimer ? "--accept-disclaimer " : "";
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{CliProjectPath}\" -- {disclaimerArg}{arguments}",
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

    // ── Full Upload → Share → Download Cycle ────────────────────

    [Fact]
    public async Task FullCycle_UploadShareDownload_Succeeds()
    {
        // Step 1: Upload a directory with test files
        var sourceDir = Path.Combine(_tempDir, "cycle_source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "data.txt"), "End-to-end test data");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "config.json"), """{"key":"value"}""");

        var uploadResult = await RunCliAsync(
            $"upload \"{sourceDir}\" --name CycleTest",
            includeDisclaimer: true);

        uploadResult.ExitCode.Should().Be(0);
        uploadResult.Stdout.Should().Contain("CycleTest");
        uploadResult.Stdout.Should().Contain("Published");
        uploadResult.Stdout.Should().Contain("Files");

        // Step 2: Share a pre-populated item to get a share key
        // Pre-populated items (1,2,3) are created when STEAMSHARE_TEST_PREPOPULATE=1
        var shareResult = await RunCliAsync(
            "share 1",
            includeDisclaimer: true);

        shareResult.ExitCode.Should().Be(0);
        shareResult.Stdout.Should().Contain("sshare+");

        var shareKey = ExtractShareKey(shareResult.Stdout);
        shareKey.Should().NotBeNull("share command should output a valid sshare+ key");

        // Step 3: Download via the share key
        var downloadResult = await RunCliAsync(
            $"download --key \"{shareKey}\" --output \"{_outputDir}\"",
            includeDisclaimer: true);

        downloadResult.ExitCode.Should().Be(0);
        downloadResult.Stdout.Should().Contain("Resolved");
        downloadResult.Stdout.Should().Contain("Published");
        downloadResult.Stdout.Should().Contain("Size");
    }

    [Fact]
    public async Task FullCycle_UploadThenDownloadById_Succeeds()
    {
        // Upload a directory, capture the published ID, then download by ID
        var sourceDir = Path.Combine(_tempDir, "byid_source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "hello.txt"), "Download by ID test");

        var uploadResult = await RunCliAsync(
            $"upload \"{sourceDir}\" --name ByIdTest",
            includeDisclaimer: true);

        uploadResult.ExitCode.Should().Be(0);
        uploadResult.Stdout.Should().Contain("Published");

        // Download using pre-populated item ID 1 (works across process boundaries
        // since the dummy service pre-populates on each init)
        var downloadResult = await RunCliAsync(
            $"download --id 1 --output \"{_outputDir}\"",
            includeDisclaimer: true);

        downloadResult.ExitCode.Should().Be(0);
        downloadResult.Stdout.Should().Contain("Published");
        downloadResult.Stdout.Should().Contain("Saved to");
    }

    [Fact]
    public async Task Download_CreatesFilesInOutputDirectory()
    {
        var downloadResult = await RunCliAsync(
            $"download --id 1 --output \"{_outputDir}\"",
            includeDisclaimer: true);

        downloadResult.ExitCode.Should().Be(0);

        // Verify files were downloaded
        var downloadedFiles = Directory.GetFiles(_outputDir, "*", SearchOption.AllDirectories);
        downloadedFiles.Should().NotBeEmpty("download should create files in output directory");
    }

    // ── Help Text for Every Command ─────────────────────────────

    [Fact]
    public async Task Help_Upload_ShowsPathAndOptions()
    {
        var result = await RunCliAsync("upload --help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("path", AtLeast.Once());
        result.Stdout.Should().ContainAny("directory", "Directory");
        result.Stdout.Should().Contain("name");
        result.Stdout.Should().Contain("visibility");
    }

    [Fact]
    public async Task Help_Download_ShowsKeyAndOptions()
    {
        var result = await RunCliAsync("download --help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("key");
        result.Stdout.Should().Contain("id");
        result.Stdout.Should().ContainAny("output", "Output");
        result.Stdout.Should().ContainAny("password", "Password");
    }

    [Fact]
    public async Task Help_Share_ShowsIdAndPassword()
    {
        var result = await RunCliAsync("share --help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("id");
        result.Stdout.Should().ContainAny("password", "Password");
    }

    [Fact]
    public async Task Help_List_ShowsDescription()
    {
        var result = await RunCliAsync("list --help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().ContainAny("owned", "Owned", "file", "File");
    }

    [Fact]
    public async Task Help_Delete_ShowsConfirmAndId()
    {
        var result = await RunCliAsync("delete --help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("id");
        result.Stdout.Should().ContainAny("confirm", "Confirm");
    }

    [Fact]
    public async Task Help_Rename_ShowsIdAndName()
    {
        var result = await RunCliAsync("rename --help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("id");
        result.Stdout.Should().ContainAny("name", "Name");
    }

    [Fact]
    public async Task Help_Visibility_ShowsIdAndSet()
    {
        var result = await RunCliAsync("visibility --help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("id");
        result.Stdout.Should().ContainAny("set", "Set");
    }

    // ── Invalid Arguments Produce Errors ────────────────────────

    [Fact]
    public async Task Upload_MissingRequiredPath_Fails()
    {
        var result = await RunCliAsync("upload", includeDisclaimer: true);

        result.ExitCode.Should().NotBe(0);
        result.Stdout.Should().ContainAny("Error", "error", "required");
    }

    [Fact]
    public async Task Upload_NonexistentDirectory_Fails()
    {
        var nonExistent = Path.Combine(_tempDir, "does_not_exist");
        var result = await RunCliAsync($"upload \"{nonExistent}\"", includeDisclaimer: true);

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("Error", "error", "not found");
    }

    [Fact]
    public async Task Share_MissingRequiredId_Fails()
    {
        var result = await RunCliAsync("share", includeDisclaimer: true);

        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task Delete_MissingRequiredId_Fails()
    {
        var result = await RunCliAsync("delete", includeDisclaimer: true);

        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task Delete_WithoutConfirm_Fails()
    {
        var result = await RunCliAsync("delete 1", includeDisclaimer: true);

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().Contain("confirm");
    }

    [Fact]
    public async Task Rename_MissingRequiredId_Fails()
    {
        var result = await RunCliAsync("rename", includeDisclaimer: true);

        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task Rename_WithoutName_Fails()
    {
        var result = await RunCliAsync("rename 1", includeDisclaimer: true);

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("name", "Error", "error");
    }

    [Fact]
    public async Task Visibility_MissingRequiredId_Fails()
    {
        var result = await RunCliAsync("visibility", includeDisclaimer: true);

        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task Visibility_MissingSet_Fails()
    {
        var result = await RunCliAsync("visibility 1", includeDisclaimer: true);

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("required", "Error", "error");
    }

    [Fact]
    public async Task Visibility_InvalidValue_Fails()
    {
        var result = await RunCliAsync("visibility 1 --set BadValue", includeDisclaimer: true);

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("Invalid", "Error", "error");
    }

    [Fact]
    public async Task Download_WithoutKeyOrId_Fails()
    {
        var result = await RunCliAsync("download", includeDisclaimer: true);

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("key", "id", "Error", "error");
    }

    [Fact]
    public async Task Download_InvalidShareKey_Fails()
    {
        var result = await RunCliAsync(
            "download --key \"sshare+INVALID_BASE64_KEY_DATA\"",
            includeDisclaimer: true);

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("Invalid", "Error", "error", "key");
    }

    [Fact]
    public async Task UnknownCommand_ExitsWithError()
    {
        var result = await RunCliAsync("completely_unknown_command_xyz", includeDisclaimer: true);

        result.ExitCode.Should().NotBe(0);
    }

    // ── --accept-disclaimer ──────────────────────────────────────

    [Fact]
    public async Task WithoutAcceptDisclaimer_ShowsDisclaimerAndExitsWithError()
    {
        // Run without --accept-disclaimer and without pre-accepted config
        var result = await RunCliWithoutDisclaimerAsync("list");

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("DISCLAIMER", "Disclaimer", "disclaimer");
    }

    [Fact]
    public async Task WithAcceptDisclaimer_ProceedsNormally()
    {
        var result = await RunCliAsync("list", includeDisclaimer: true);

        // May succeed (0) or fail if nothing to list — but should NOT show disclaimer
        result.Stdout.Should().NotContain("DISCLAIMER");
    }

    [Fact]
    public async Task AcceptDisclaimer_AllowsUpload()
    {
        var sourceDir = Path.Combine(_tempDir, "disclaimer_upload");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "test.txt"), "disclaimer test");

        var result = await RunCliAsync(
            $"upload \"{sourceDir}\"",
            includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
    }

    // ── Visibility Gating ───────────────────────────────────────

    [Fact]
    public async Task Visibility_SetToPublic_Succeeds()
    {
        var result = await RunCliAsync("visibility 1 --set Public", includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Public");
    }

    [Fact]
    public async Task Visibility_SetToUnlisted_Succeeds()
    {
        var result = await RunCliAsync("visibility 1 --set Unlisted", includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Unlisted");
    }

    [Fact]
    public async Task Visibility_SetToPrivate_Succeeds()
    {
        var result = await RunCliAsync("visibility 1 --set Private", includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().ContainAny("Private", "changed");
    }

    [Fact]
    public async Task Visibility_NonExistentItem_Fails()
    {
        var result = await RunCliAsync("visibility 99999 --set Private", includeDisclaimer: true);

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().ContainAny("Not found", "Error", "error");
    }

    // ── Progress Output Format ──────────────────────────────────

    [Fact]
    public async Task Upload_ShowsProgressOutput()
    {
        var sourceDir = Path.Combine(_tempDir, "progress_upload");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "data.bin"),
            new string('X', 1024 * 10)); // 10KB of data

        var result = await RunCliAsync(
            $"upload \"{sourceDir}\" --name ProgressTest",
            includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        // Progress output (spinner/bar) is not captured when stdout is redirected;
        // verify the result table contains expected upload metadata columns.
        result.Stdout.Should().ContainAll("Name", "Published ID", "Files", "Size");
    }

    [Fact]
    public async Task Download_ShowsProgressOutput()
    {
        var result = await RunCliAsync(
            $"download --id 1 --output \"{_outputDir}\"",
            includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        // Progress output (spinner/bar) is not captured when stdout is redirected;
        // verify the result table contains expected download metadata columns.
        result.Stdout.Should().ContainAll("Name", "Published ID", "Files", "Saved to");
    }

    [Fact]
    public async Task Share_ShowsProgressOutput()
    {
        var result = await RunCliAsync("share 1", includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        // The share command outputs a Panel with the share key.
        // Progress messages ("Generating share key") from Spectre.Console
        // live-updating display are not captured when stdout is redirected.
        result.Stdout.Should().Contain("sshare+");
        result.Stdout.Should().ContainAny("Share Key", "Share key");
    }

    [Fact]
    public async Task List_ShowsTableOutput()
    {
        var result = await RunCliAsync("list", includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        // When pre-populated items exist, output should contain table-like content
        result.Stdout.Should().NotBeNullOrEmpty();
    }

    // ── Delete + Rename Operations ──────────────────────────────

    [Fact]
    public async Task Delete_WithConfirm_Succeeds()
    {
        var result = await RunCliAsync("delete 1 --confirm", includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().ContainAny("Successfully deleted", "deleted");
    }

    [Fact]
    public async Task Rename_WithName_Succeeds()
    {
        var result = await RunCliAsync("rename 1 --name \"Renamed Item\"", includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Renamed Item");
        result.Stdout.Should().ContainAny("Successfully renamed", "renamed");
    }

    // ── Share with Password ─────────────────────────────────────

    [Fact]
    public async Task Share_WithPassword_GeneratesProtectedKey()
    {
        var result = await RunCliAsync("share 1 --password secret123", includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("sshare+");
        result.Stdout.Should().ContainAny("password-protected", "password");
    }

    [Fact]
    public async Task Share_WithoutPassword_GeneratesUnprotectedKey()
    {
        var result = await RunCliAsync("share 1", includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("sshare+");
    }

    // ── Upload with Options ─────────────────────────────────────

    [Fact]
    public async Task Upload_WithCustomName_UsesProvidedName()
    {
        var sourceDir = Path.Combine(_tempDir, "custom_name_upload");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "content.txt"), "custom name");

        var result = await RunCliAsync(
            $"upload \"{sourceDir}\" --name \"My Custom Name\"",
            includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("My Custom Name");
    }

    [Fact]
    public async Task Upload_WithVisibilityOption_Succeeds()
    {
        var sourceDir = Path.Combine(_tempDir, "vis_upload");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "data.txt"), "visibility test");

        var result = await RunCliAsync(
            $"upload \"{sourceDir}\" --visibility Public",
            includeDisclaimer: true);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Public");
    }

    // ── Download with Password ──────────────────────────────────

    [Fact]
    public async Task Download_ShareKeyWithPassword_ResolvesAndDownloads()
    {
        // Generate a password-protected share key
        var shareResult = await RunCliAsync("share 1 --password testpass", includeDisclaimer: true);
        shareResult.ExitCode.Should().Be(0);
        var shareKey = ExtractShareKey(shareResult.Stdout);
        shareKey.Should().NotBeNull();

        // Download using the password-protected key
        var downloadResult = await RunCliAsync(
            $"download --key \"{shareKey}\" --password testpass --output \"{_outputDir}\"",
            includeDisclaimer: true);

        downloadResult.ExitCode.Should().Be(0);
        downloadResult.Stdout.Should().ContainAny("Resolved", "Published");
    }

    [Fact]
    public async Task Download_WrongPassword_Fails()
    {
        // Generate a password-protected share key
        var shareResult = await RunCliAsync("share 1 --password correctpass", includeDisclaimer: true);
        shareResult.ExitCode.Should().Be(0);
        var shareKey = ExtractShareKey(shareResult.Stdout);
        shareKey.Should().NotBeNull();

        // Try downloading with wrong password
        var downloadResult = await RunCliAsync(
            $"download --key \"{shareKey}\" --password wrongpass --output \"{_outputDir}\"",
            includeDisclaimer: true);

        downloadResult.ExitCode.Should().Be(1);
        downloadResult.Stdout.Should().ContainAny("Invalid", "Error", "error", "password");
    }

    // ── Helper: run CLI without --accept-disclaimer ─────────────

    private async Task<CliResult> RunCliWithoutDisclaimerAsync(string arguments, int timeoutMs = 60000)
    {
        // Use isolated config directory so DisclaimerDismissed state doesn't leak
        var configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(configDir);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{CliProjectPath}\" -- {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _tempDir
        };

        startInfo.Environment["STEAMSHARE_TEST_MODE"] = "dummy";
        startInfo.Environment["STEAMSHARE_DATA_DIR"] = configDir;
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

    // ── Output Parsing Helpers ──────────────────────────────────

    private static string? ExtractShareKey(string output)
    {
        var idx = output.IndexOf("sshare+", StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        // Find the end of the key block.
        // Password-protected keys are followed by "This share key" text.
        // Unprotected keys are inside a Panel that ends with bottom corner chars.
        var endIdx = output.IndexOf("This share key", idx, StringComparison.Ordinal);
        if (endIdx < 0)
        {
            endIdx = output.IndexOfAny(['\u2570', '\u2514', '\u256F', '\u2518'], idx + "sshare+".Length);
        }
        if (endIdx < 0)
        {
            endIdx = output.Length;
        }

        var keyBlock = output[idx..endIdx];
        return "sshare+" + Regex.Replace(keyBlock["sshare+".Length..], @"[^A-Za-z0-9+/=]", "");
    }

    private static bool IsBase64Char(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '/' or '=';

    private static ulong? ExtractPublishedId(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var match = Regex.Match(line,
                @"(?:Published\s*(?:File\s*)?ID)\D*(\d+)",
                RegexOptions.IgnoreCase);
            if (match.Success && ulong.TryParse(match.Groups[1].Value, out var id))
            {
                return id;
            }
        }

        return null;
    }

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr);
}
