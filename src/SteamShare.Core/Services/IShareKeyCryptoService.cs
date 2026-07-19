using SteamShare.Core.Models;

namespace SteamShare.Core.Services;

/// <summary>
/// Provides cryptographic operations for generating and parsing
/// SteamShare share keys. Share keys encode a Workshop item's
/// published file ID and may be optionally protected with a
/// password using AES-256-GCM encryption.
/// </summary>
/// <remarks>
/// Share keys use the <c>sshare+</c> URI scheme prefix followed
/// by a Base64-encoded, LZ4-compressed payload. When password-
/// protected, the file ID is encrypted with a key derived via
/// PBKDF2 (RFC 2898) and the payload carries an
/// <see cref="ShareKeyPayload.Encrypted"/> flag set to <c>true</c>.
/// </remarks>
public interface IShareKeyCryptoService
{
    /// <summary>
    /// Generates a share key for the specified Steam Workshop item.
    /// The resulting key can be distributed to other users to grant
    /// them access to download the item.
    /// </summary>
    /// <param name="publishedFileId">
    /// The published file ID of the Workshop item to encode in the share key.
    /// </param>
    /// <param name="password">
    /// Optional password to encrypt the share key payload.
    /// When provided, the generated key is protected with AES-256-GCM
    /// and requires the same password to parse. Pass <c>null</c>
    /// (or omit) to produce an unencrypted share key.
    /// </param>
    /// <returns>
    /// A share key string with the <c>sshare+</c> prefix, e.g.
    /// <c>sshare+UEsDBBQAAAAI...</c>.
    /// </returns>
    string GenerateShareKey(ulong publishedFileId, string? password = null);

    /// <summary>
    /// Parses a share key string and extracts the Workshop item
    /// published file ID. If the key is encrypted, the correct
    /// password must be provided to decrypt the payload.
    /// </summary>
    /// <param name="shareKey">
    /// The share key string to parse, including the <c>sshare+</c> prefix.
    /// </param>
    /// <param name="password">
    /// The password to use for decryption. Required only if the
    /// share key was generated with a password. Pass <c>null</c>
    /// for unencrypted keys.
    /// </param>
    /// <returns>
    /// A <see cref="ShareKeyPayload"/> containing the parsed
    /// published file ID and encryption status.
    /// </returns>
    ShareKeyPayload ParseShareKey(string shareKey, string? password = null);
}
