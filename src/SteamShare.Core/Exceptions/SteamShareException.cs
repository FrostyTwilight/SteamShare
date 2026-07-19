namespace SteamShare.Core.Exceptions;

/// <summary>
/// Base exception for all SteamShare domain errors.
/// </summary>
public abstract class SteamShareException : Exception
{
    /// <summary>
    /// User-friendly message key for localization lookup.
    /// </summary>
    public string UserFriendlyMessageKey { get; }

    protected SteamShareException(string message, string userFriendlyMessageKey = "")
        : base(message)
    {
        UserFriendlyMessageKey = userFriendlyMessageKey;
    }

    protected SteamShareException(string message, Exception inner, string userFriendlyMessageKey = "")
        : base(message, inner)
    {
        UserFriendlyMessageKey = userFriendlyMessageKey;
    }
}

/// <summary>Steam client is not running or user is not logged in.</summary>
public sealed class SteamNotRunningException : SteamShareException
{
    public SteamNotRunningException() : base("Steam client is not running", "Error_SteamNotRunning") { }
    public SteamNotRunningException(string message) : base(message, "Error_SteamNotRunning") { }
}

/// <summary>Failed to upload content to the Steam Workshop.</summary>
public sealed class WorkshopUploadException : SteamShareException
{
    public WorkshopUploadException(string message, Exception? inner = null)
        : base(message, inner ?? new Exception(), "Error_WorkshopUpload") { }
}

/// <summary>Failed to download content from the Steam Workshop.</summary>
public sealed class WorkshopDownloadException : SteamShareException
{
    public WorkshopDownloadException(string message, Exception? inner = null)
        : base(message, inner ?? new Exception(), "Error_WorkshopDownload") { }
}

/// <summary>Share key string could not be parsed.</summary>
public sealed class ShareKeyParseException : SteamShareException
{
    public ShareKeyParseException(string message) : base(message, "Error_ShareKeyParse") { }
    public ShareKeyParseException(string message, Exception inner) : base(message, inner, "Error_ShareKeyParse") { }
}

/// <summary>Share key decryption or encryption failed.</summary>
public sealed class ShareKeyCryptoException : SteamShareException
{
    public ShareKeyCryptoException(string message, Exception? inner = null)
        : base(message, inner ?? new Exception(), "Error_ShareKeyCrypto") { }
}

/// <summary>Referenced file group does not exist.</summary>
public sealed class FileGroupNotFoundException : SteamShareException
{
    public FileGroupNotFoundException(ulong publishedFileId)
        : base($"File group {publishedFileId} not found", "Error_FileGroupNotFound") { }
}

/// <summary>Failed to load or save configuration.</summary>
public sealed class ConfigLoadException : SteamShareException
{
    public ConfigLoadException(string message, Exception? inner = null)
        : base(message, inner ?? new Exception(), "Error_ConfigLoad") { }
}

/// <summary>Workshop item metadata exceeds size limit.</summary>
public sealed class MetadataOverflowException : SteamShareException
{
    public MetadataOverflowException(int actualSizeBytes, int maxSizeBytes)
        : base($"Metadata size {actualSizeBytes} exceeds limit {maxSizeBytes}", "Error_MetadataOverflow") { }
}
