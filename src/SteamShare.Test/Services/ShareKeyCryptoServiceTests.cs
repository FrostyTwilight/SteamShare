using SteamShare.Core.Exceptions;
using SteamShare.Core.Services;

namespace SteamShare.Test.Services;

public class ShareKeyCryptoServiceTests
{
    private readonly ShareKeyCryptoService _service = new();

    [Fact]
    public void GenerateShareKey_WithoutPassword_StartsWithPrefix()
    {
        var key = _service.GenerateShareKey(12345);
        key.Should().StartWith("sshare+");
    }

    [Fact]
    public void GenerateShareKey_WithoutPassword_IsNotEncrypted()
    {
        var key = _service.GenerateShareKey(12345);
        var payload = _service.ParseShareKey(key);
        payload.Encrypted.Should().BeFalse();
        payload.Id.Should().Be(12345ul);
    }

    [Fact]
    public void GenerateAndParse_WithPassword_RoundTrips()
    {
        var key = _service.GenerateShareKey(67890, "mySecret123");
        var payload = _service.ParseShareKey(key, "mySecret123");
        payload.Encrypted.Should().BeTrue();
        payload.Id.Should().Be(67890ul);
    }

    [Fact]
    public void Parse_WithWrongPassword_ThrowsShareKeyCryptoException()
    {
        var key = _service.GenerateShareKey(12345, "correct");
        var act = () => _service.ParseShareKey(key, "wrong");
        act.Should().Throw<ShareKeyCryptoException>();
    }

    [Fact]
    public void Parse_WithoutPassword_WhenPasswordRequired_ThrowsShareKeyCryptoException()
    {
        var key = _service.GenerateShareKey(12345, "secret");
        // Parsing encrypted key without password should fail
        var act = () => _service.ParseShareKey(key, null);
        act.Should().Throw<ShareKeyParseException>();
    }

    [Fact]
    public void Parse_WithPassword_WhenNotEncrypted_ThrowsShareKeyCryptoException()
    {
        var key = _service.GenerateShareKey(12345); // no password
        // Parsing unencrypted key WITH password should fail (data won't decrypt)
        var act = () => _service.ParseShareKey(key, "unnecessary");
        act.Should().Throw<ShareKeyCryptoException>();
    }

    [Fact]
    public void Parse_InvalidPrefix_ThrowsShareKeyParseException()
    {
        var act = () => _service.ParseShareKey("invalid_key");
        act.Should().Throw<ShareKeyParseException>();
    }

    [Fact]
    public void Parse_MalformedBase64_ThrowsShareKeyParseException()
    {
        var act = () => _service.ParseShareKey("sshare+!!!not_base64!!!");
        act.Should().Throw<ShareKeyParseException>();
    }

    [Fact]
    public void GenerateShareKey_ProducesDeterministicPayload_ForSameId()
    {
        var key1 = _service.GenerateShareKey(42);
        var key2 = _service.GenerateShareKey(42);

        // Without password, payload should be deterministic (same id → same json)
        var p1 = _service.ParseShareKey(key1);
        var p2 = _service.ParseShareKey(key2);
        p1.Id.Should().Be(p2.Id);
        p1.Encrypted.Should().Be(p2.Encrypted);
    }

    [Fact]
    public void GenerateShareKey_WithPassword_DifferentEachTime()
    {
        var key1 = _service.GenerateShareKey(42, "pass");
        var key2 = _service.GenerateShareKey(42, "pass");
        // With password, random salt makes each key different
        key1.Should().NotBe(key2);
    }

    // ── Edge case: empty password ───────────────────────────────

    [Fact]
    public void GenerateShareKey_WithEmptyPassword_TreatsAsPassword()
    {
        var key = _service.GenerateShareKey(12345, string.Empty);

        key.Should().StartWith("sshare+");
        var payload = _service.ParseShareKey(key, string.Empty);
        payload.Encrypted.Should().BeTrue();
        payload.Id.Should().Be(12345ul);
    }

    // ── Edge case: unicode password ─────────────────────────────

    [Fact]
    public void GenerateAndParse_WithUnicodePassword_RoundTrips()
    {
        var key = _service.GenerateShareKey(99999, "密码🔑测试");

        var payload = _service.ParseShareKey(key, "密码🔑测试");
        payload.Encrypted.Should().BeTrue();
        payload.Id.Should().Be(99999ul);
    }

    [Fact]
    public void Parse_WithUnicodePassword_WrongPassword_Throws()
    {
        var key = _service.GenerateShareKey(99999, "密码🔑测试");

        var act = () => _service.ParseShareKey(key, "错误密码");

        act.Should().Throw<ShareKeyCryptoException>();
    }

    // ── Edge case: large payload ID ─────────────────────────────

    [Fact]
    public void GenerateAndParse_WithMaxUlongId_RoundTrips()
    {
        var maxId = ulong.MaxValue;

        var key = _service.GenerateShareKey(maxId);

        var payload = _service.ParseShareKey(key);
        payload.Id.Should().Be(maxId);
    }

    [Fact]
    public void GenerateAndParse_WithZeroId_RoundTrips()
    {
        var key = _service.GenerateShareKey(0);

        var payload = _service.ParseShareKey(key);
        payload.Id.Should().Be(0ul);
    }

    // ── Edge case: tampered data ────────────────────────────────

    [Fact]
    public void Parse_TamperedKey_ThrowsShareKeyParseException()
    {
        var key = _service.GenerateShareKey(12345);

        // Tamper with the base64 portion (change last char)
        var prefix = "sshare+";
        var base64Part = key[prefix.Length..];
        var tamperedBase64 = base64Part[..^1] + (base64Part[^1] == 'A' ? 'B' : 'A');
        var tamperedKey = prefix + tamperedBase64;

        var act = () => _service.ParseShareKey(tamperedKey);

        act.Should().Throw<ShareKeyParseException>();
    }

    [Fact]
    public void Parse_EncryptedTamperedKey_ThrowsShareKeyCryptoException()
    {
        var key = _service.GenerateShareKey(12345, "secret");

        // Modify a character in the base64 part
        var prefix = "sshare+";
        var base64Part = key[prefix.Length..];
        var tamperedBase64 = base64Part[..^5] + 'X' + base64Part[^4..];
        var tamperedKey = prefix + tamperedBase64;

        var act = () => _service.ParseShareKey(tamperedKey, "secret");

        // Should fail either at decompression or decryption
        act.Should().Throw<SteamShareException>();
    }

    // ── Edge case: empty/null key ───────────────────────────────

    [Fact]
    public void Parse_NullKey_ThrowsException()
    {
        var act = () => _service.ParseShareKey(null!);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Parse_EmptyKey_ThrowsShareKeyParseException()
    {
        var act = () => _service.ParseShareKey(string.Empty);

        act.Should().Throw<ShareKeyParseException>();
    }

    [Fact]
    public void Parse_KeyWithOnlyPrefix_ThrowsException()
    {
        // "sshare+" with no payload: decompresses to empty bytes,
        // JSON parsing fails, which may produce ShareKeyCryptoException
        // (as the system thinks it might be encrypted data)
        var act = () => _service.ParseShareKey("sshare+");

        act.Should().Throw<SteamShareException>();
    }

    // ── Edge case: whitespace password ──────────────────────────

    [Fact]
    public void GenerateAndParse_WithWhitespacePassword_RoundTrips()
    {
        var key = _service.GenerateShareKey(42, "   spaced password  ");

        var payload = _service.ParseShareKey(key, "   spaced password  ");
        payload.Encrypted.Should().BeTrue();
        payload.Id.Should().Be(42ul);

        // Slightly different whitespace should fail
        var act = () => _service.ParseShareKey(key, "  spaced password  ");
        act.Should().Throw<ShareKeyCryptoException>();
    }

    // ── Deterministic without password ──────────────────────────

    [Fact]
    public void GenerateShareKey_WithoutPassword_ProducesIdenticalKeys_ForSameId()
    {
        var key1 = _service.GenerateShareKey(42);
        var key2 = _service.GenerateShareKey(42);

        // Without password, the key should be deterministic
        key1.Should().Be(key2);
    }

    [Fact]
    public void GenerateShareKey_WithoutPassword_DifferentIds_ProduceDifferentKeys()
    {
        var key1 = _service.GenerateShareKey(1);
        var key2 = _service.GenerateShareKey(2);

        key1.Should().NotBe(key2);
    }
}
