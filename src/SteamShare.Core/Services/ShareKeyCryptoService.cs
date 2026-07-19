using System.Security.Cryptography;
using System.Text;

using K4os.Compression.LZ4;

using Serilog;

using SteamShare.Core.Exceptions;
using SteamShare.Core.Models;

namespace SteamShare.Core.Services;

public class ShareKeyCryptoService : IShareKeyCryptoService
{
    private static readonly ILogger LogSerilog = Log.ForContext<ShareKeyCryptoService>();
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 600_000;
    private const string ShareKeyPrefix = "sshare+";

    public string GenerateShareKey(ulong publishedFileId, string? password = null)
    {
        LogSerilog.Debug("Generating share key for {PublishedFileId} (encrypted={Encrypted})",
            publishedFileId, password != null);

        var payload = new ShareKeyPayload
        {
            Encrypted = password != null,
            Id = publishedFileId
        };

        var json = payload.ToJson();
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] compressed = LZ4Pickler.Pickle(jsonBytes);

        if (password != null)
        {
            jsonBytes = Encrypt(compressed, password);
            LogSerilog.Debug("Share key payload encrypted");
        }
        else
        {
            jsonBytes = compressed;
        }


        var key = ShareKeyPrefix + Convert.ToBase64String(jsonBytes);
        LogSerilog.Debug("Share key generated (length={Length})", key.Length);
        return key;
    }

    public ShareKeyPayload ParseShareKey(string shareKey, string? password = null)
    {
        LogSerilog.Debug("Parsing share key (length={Length}, hasPassword={HasPassword})",
            shareKey.Length, password != null);

        if (!shareKey.StartsWith(ShareKeyPrefix))
        {
            LogSerilog.Error("Share key parse failed: missing prefix");
            throw new ShareKeyParseException($"Share key must start with '{ShareKeyPrefix}'");
        }

        string base64 = shareKey[ShareKeyPrefix.Length..];

        byte[] compressed;
        try
        {
            compressed = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            LogSerilog.Error(ex, "Share key parse failed: invalid Base64");
            throw new ShareKeyParseException("Invalid Base64 in share key", ex);
        }

        if (password != null)
        {
            compressed = Decrypt(compressed, password);
            LogSerilog.Debug("Share key payload decrypted");
        }

        byte[] jsonBytes;
        try
        {
            jsonBytes = LZ4Pickler.Unpickle(compressed);
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Share key parse failed: decompression error");
            throw new ShareKeyParseException("Failed to decompress share key payload", ex);
        }

        string json;
        try
        {
            json = Encoding.UTF8.GetString(jsonBytes);
            var payload = ShareKeyPayload.FromJson(json);
            LogSerilog.Debug("Share key parsed: item {PublishedFileId}", payload.Id);
            return payload;
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            if (password == null)
            {
                LogSerilog.Error(ex, "Share key appears to be encrypted but no password provided");
                throw new ShareKeyCryptoException(
                    "Share key appears to be encrypted but no password was provided", ex);
            }

            LogSerilog.Error(ex, "Share key payload is corrupt — decryption likely failed");
            throw new ShareKeyCryptoException(
                "Decryption failed — password may be incorrect or data was tampered with", ex);
        }
    }

    private static byte[] Encrypt(byte[] plaintext, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations,
            HashAlgorithmName.SHA256, KeySize);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        // Format: salt || nonce || tag || ciphertext
        byte[] result = new byte[SaltSize + NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(nonce, 0, result, SaltSize, NonceSize);
        Buffer.BlockCopy(tag, 0, result, SaltSize + NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, SaltSize + NonceSize + TagSize, ciphertext.Length);

        return result;
    }

    private static byte[] Decrypt(byte[] encryptedData, string password)
    {
        if (encryptedData.Length < SaltSize + NonceSize + TagSize)
        {
            throw new ShareKeyCryptoException("Encrypted data is too short");
        }

        byte[] salt = encryptedData[..SaltSize];
        byte[] nonce = encryptedData[SaltSize..(SaltSize + NonceSize)];
        byte[] tag = encryptedData[(SaltSize + NonceSize)..(SaltSize + NonceSize + TagSize)];
        byte[] ciphertext = encryptedData[(SaltSize + NonceSize + TagSize)..];

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations,
            HashAlgorithmName.SHA256, KeySize);

        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new ShareKeyCryptoException(
                "Decryption failed — password may be incorrect or data was tampered with", ex);
        }

        return plaintext;
    }
}
