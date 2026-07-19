using System.Threading;

namespace SteamShare.Core.Utilities;

/// <summary>
/// Strategy for file copying/linking operations.
/// </summary>
public enum CopyStrategy
{
    /// <summary>Try hard link first, fall back to symlink, then copy.</summary>
    Auto,
    /// <summary>Force a full file copy.</summary>
    ForceCopy,
    /// <summary>Try hard link only, fail if not possible.</summary>
    HardLinkOnly
}

/// <summary>
/// Result of a directory copy operation.
/// </summary>
public readonly struct DirectoryCopyResult
{
    /// <summary>Number of files that were hardlinked.</summary>
    public int FilesHardlinked { get; init; }

    /// <summary>Number of files that were fully copied.</summary>
    public int FilesCopied { get; init; }

    /// <summary>Number of directories created in the target.</summary>
    public int DirectoriesCreated { get; init; }
}

/// <summary>
/// Platform-adaptive file system utilities.
/// Attempts hard links first, then symbolic links, then falls back to file copy.
/// </summary>
public static class FileSystemHelper
{
    /// <summary>
    /// Copy or link a file from source to destination.
    /// Attempts: hard link → symbolic link → file copy.
    /// Logs which strategy was used.
    /// </summary>
    /// <returns>The strategy that was actually used.</returns>
    public static async Task<CopyStrategy> CopyOrLinkAsync(
        string sourcePath, string destinationPath,
        CopyStrategy strategy = CopyStrategy.Auto,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(destinationPath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source file not found", sourcePath);
        }

        // Ensure destination directory exists
        var destDir = Path.GetDirectoryName(destinationPath);
        if (destDir is not null && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // Remove destination if it already exists
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        if (strategy == CopyStrategy.ForceCopy)
        {
            await CopyFileAsync(sourcePath, destinationPath, ct);
            return CopyStrategy.ForceCopy;
        }

        // Try hard link first (fastest, zero additional disk space)
        if (TryCreateHardLink(sourcePath, destinationPath))
        {
            Serilog.Log.Debug("Created hard link: {Source} → {Dest}", sourcePath, destinationPath);
            return strategy == CopyStrategy.HardLinkOnly ? CopyStrategy.HardLinkOnly : CopyStrategy.Auto;
        }

        // HardLinkOnly strategy: fail if hard link was not possible
        if (strategy == CopyStrategy.HardLinkOnly)
        {
            throw new InvalidOperationException(
                $"Failed to create hard link from '{sourcePath}' to '{destinationPath}'. " +
                "Hard links require source and destination to be on the same volume.");
        }

        // Try symbolic link (works across volumes on Windows with permissions)
        if (TryCreateSymbolicLink(sourcePath, destinationPath))
        {
            Serilog.Log.Debug("Created symbolic link: {Source} → {Dest}", sourcePath, destinationPath);
            return CopyStrategy.Auto;
        }

        // Fall back to file copy
        Serilog.Log.Debug("Falling back to file copy: {Source} → {Dest}", sourcePath, destinationPath);
        await CopyFileAsync(sourcePath, destinationPath, ct);
        return CopyStrategy.ForceCopy;
    }

    /// <summary>
    /// Copy an entire directory tree to a target location.
    /// Uses hard links where possible, falls back to file copy.
    /// Does NOT use symbolic links.
    /// </summary>
    /// <param name="sourceDir">Source directory path.</param>
    /// <param name="targetDir">Target directory path.</param>
    /// <param name="deleteSourceAfterCopy">If true, deletes the source directory and all its contents after a successful copy.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result with counts of hardlinked files, copied files, and created directories.</returns>
    public static async Task<DirectoryCopyResult> CopyDirectoryAsync(
        string sourceDir, string targetDir,
        bool deleteSourceAfterCopy = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDir);
        ArgumentNullException.ThrowIfNull(targetDir);

        if (!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(targetDir);

        var filesHardlinked = 0;
        var filesCopied = 0;
        var directoriesCreated = 1; // targetDir itself

        // Create all subdirectories first (handles empty directories)
        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDir, dir);
            var targetSubDir = Path.Combine(targetDir, relativePath);
            Directory.CreateDirectory(targetSubDir);
            directoriesCreated++;
        }

        // Process all files: try hardlink first, fall back to file copy
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        var maxConcurrency = Math.Min(Environment.ProcessorCount, 8);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrency,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(files, parallelOptions, async (file, innerCt) =>
        {
            innerCt.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var targetFile = Path.Combine(targetDir, relativePath);

            // Remove destination if it already exists
            if (File.Exists(targetFile))
            {
                File.Delete(targetFile);
            }

            // Try hard link first (fastest, zero additional disk space)
            if (TryCreateHardLink(file, targetFile))
            {
                Serilog.Log.Debug("CopyDirectory: hardlinked {Source} → {Dest}", file, targetFile);
                Interlocked.Increment(ref filesHardlinked);
            }
            else
            {
                // Fall back to file copy
                Serilog.Log.Debug("CopyDirectory: copying {Source} → {Dest}", file, targetFile);
                await CopyFileAsync(file, targetFile, innerCt).ConfigureAwait(false);
                Interlocked.Increment(ref filesCopied);
            }
        });

        // Delete source directory if requested
        if (deleteSourceAfterCopy)
        {
            Directory.Delete(sourceDir, recursive: true);
            Serilog.Log.Debug("CopyDirectory: deleted source after copy: {SourceDir}", sourceDir);
        }

        return new DirectoryCopyResult
        {
            FilesHardlinked = filesHardlinked,
            FilesCopied = filesCopied,
            DirectoriesCreated = directoriesCreated,
        };
    }

    private static bool TryCreateHardLink(string source, string destination)
    {
        try
        {
            File.CreateHardLink(destination, source);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateSymbolicLink(string source, string destination)
    {
        try
        {
            File.CreateSymbolicLink(destination, source);
            return File.Exists(destination);
        }
        catch
        {
            return false;
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        using var destStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await sourceStream.CopyToAsync(destStream, ct).ConfigureAwait(false);
    }

}
