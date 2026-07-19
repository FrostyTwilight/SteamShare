namespace SteamShare.Core.Models;

/// <summary>
/// Represents a parsed share key. Share key format:
/// "sshare+" followed by Base64-encoded LZ4-compressed JSON payload.
/// The payload may be encrypted with AES-256-GCM if a password was set.
/// </summary>
public sealed record ShareKey
{
    /// <summary>The workshop published file ID this key grants access to.</summary>
    public ulong PublishedFileId { get; init; }

    /// <summary>Whether the payload was encrypted with a password.</summary>
    public bool IsEncrypted { get; init; }

    /// <summary>The raw key string (sshare+...).</summary>
    public string RawString { get; init; } = string.Empty;

    /// <summary>Prefix that identifies a SteamShare share key.</summary>
    public const string Prefix = "sshare+";

    public override string ToString() => RawString;
}
