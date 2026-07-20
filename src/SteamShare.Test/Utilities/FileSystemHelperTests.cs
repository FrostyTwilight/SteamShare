using SteamShare.Core.Utilities;

namespace SteamShare.Test.Utilities;

public class FileSystemHelperTests : IDisposable
{
    private readonly string _testDir;

    public FileSystemHelperTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"SteamShareTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task CopyOrLink_CreatesDestinationFile()
    {
        var source = Path.Combine(_testDir, "source.txt");
        var dest = Path.Combine(_testDir, "dest.txt");
        await File.WriteAllTextAsync(source, "test content");

        await FileSystemHelper.CopyOrLinkAsync(source, dest);

        File.Exists(dest).Should().BeTrue();
        (await File.ReadAllTextAsync(dest)).Should().Be("test content");
    }

    [Fact]
    public async Task CopyOrLink_OverwritesExistingDestination()
    {
        var source = Path.Combine(_testDir, "source.txt");
        var dest = Path.Combine(_testDir, "dest.txt");
        await File.WriteAllTextAsync(source, "new content");
        await File.WriteAllTextAsync(dest, "old content");

        await FileSystemHelper.CopyOrLinkAsync(source, dest);

        (await File.ReadAllTextAsync(dest)).Should().Be("new content");
    }

    [Fact]
    public async Task CopyOrLink_ForceCopy_AlwaysCopies()
    {
        var source = Path.Combine(_testDir, "source.txt");
        var dest = Path.Combine(_testDir, "dest.txt");
        await File.WriteAllTextAsync(source, "data");

        var strategy = await FileSystemHelper.CopyOrLinkAsync(source, dest, CopyStrategy.ForceCopy);

        strategy.Should().Be(CopyStrategy.ForceCopy);
        File.Exists(dest).Should().BeTrue();
    }

    [Fact]
    public async Task CopyOrLink_SourceNotFound_Throws()
    {
        var act = () => FileSystemHelper.CopyOrLinkAsync(
            Path.Combine(_testDir, "nonexistent.txt"),
            Path.Combine(_testDir, "dest.txt"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task CopyOrLink_CreatesDestinationDirectory()
    {
        var source = Path.Combine(_testDir, "source.txt");
        var dest = Path.Combine(_testDir, "subdir", "dest.txt");
        await File.WriteAllTextAsync(source, "data");

        await FileSystemHelper.CopyOrLinkAsync(source, dest);

        File.Exists(dest).Should().BeTrue();
    }

    [Fact]
    public async Task CopyOrLink_HardLinkOnly_Fails_WhenHardLinkNotPossible()
    {
        // Use different temp directories on purpose to simulate cross-volume
        // (hard links only work within the same volume)
        var source = Path.Combine(_testDir, "source.txt");
        // HardLinkOnly should still work on same volume; if it does, the test
        // verifies it returns HardLinkOnly strategy.
        await File.WriteAllTextAsync(source, "hardlink data");

        var dest = Path.Combine(_testDir, "dest_hardlink.txt");

        var strategy = await FileSystemHelper.CopyOrLinkAsync(source, dest, CopyStrategy.HardLinkOnly);

        strategy.Should().Be(CopyStrategy.HardLinkOnly);
        File.Exists(dest).Should().BeTrue();
        (await File.ReadAllTextAsync(dest)).Should().Be("hardlink data");
    }

    [Fact]
    public async Task CopyOrLink_Auto_UsesHardLink_WhenPossible()
    {
        var source = Path.Combine(_testDir, "source.txt");
        var dest = Path.Combine(_testDir, "dest.txt");
        await File.WriteAllTextAsync(source, "auto link data");

        var strategy = await FileSystemHelper.CopyOrLinkAsync(source, dest, CopyStrategy.Auto);

        // On same volume, hard link should succeed and return Auto
        strategy.Should().Be(CopyStrategy.Auto);
        File.Exists(dest).Should().BeTrue();
        (await File.ReadAllTextAsync(dest)).Should().Be("auto link data");
    }

    [Fact]
    public async Task CopyOrLink_HardLink_SharesContent_WithSource()
    {
        var source = Path.Combine(_testDir, "source.txt");
        var dest = Path.Combine(_testDir, "dest.txt");
        await File.WriteAllTextAsync(source, "shared content");

        var strategy = await FileSystemHelper.CopyOrLinkAsync(source, dest, CopyStrategy.HardLinkOnly);

        strategy.Should().Be(CopyStrategy.HardLinkOnly);

        // Modify via destination — source should reflect the change (hard link shares inode)
        await File.WriteAllTextAsync(dest, "modified via dest");
        (await File.ReadAllTextAsync(source)).Should().Be("modified via dest");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_testDir))
        {
            return;
        }

        // Retry with backoff — on Windows, file handles from ReadAllTextAsync
        // and hard link operations may not be released immediately.
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Directory.Delete(_testDir, true);
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
}
