using SteamShare.Core.Exceptions;
using SteamShare.Core.Models;
using SteamShare.Core.Services;
using SteamShare.Test.Dummy;

using DummyWorkshopService = SteamShare.Test.Dummy.DummyWorkshopService;

namespace SteamShare.Test.Services;

public class ShareServiceTests
{
    private readonly DummyWorkshopService _workshop;
    private readonly ShareKeyCryptoService _crypto;
    private readonly ShareService _service;

    public ShareServiceTests()
    {
        _workshop = new DummyWorkshopService();
        _workshop.InitializeAsync().GetAwaiter().GetResult();
        _crypto = new ShareKeyCryptoService();
        _service = new ShareService(_crypto, _workshop);
    }

    [Fact]
    public async Task GenerateShareKey_CreatesValidKey()
    {
        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Test",
            Description = "Desc",
            MetadataJson = new FileGroupMetadata { Name = "Test" }.ToJson(),
            ContentPath = "/tmp/test"
        });

        var key = await _service.GenerateShareKeyAsync(id);
        key.Should().StartWith("sshare+");
    }

    [Fact]
    public async Task GenerateShareKey_PrivateItem_ChangesToUnlisted()
    {
        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Private",
            Description = "Desc",
            MetadataJson = "{}",
            ContentPath = "/tmp/test",
            Visibility = WorkshopVisibility.Private
        });

        await _service.GenerateShareKeyAsync(id);

        var item = await _workshop.QueryItemByIdAsync(id);
        item!.Visibility.Should().Be(WorkshopVisibility.Unlisted);
    }

    [Fact]
    public async Task ResolveShareKey_ReturnsMetadata()
    {
        var metadata = new FileGroupMetadata { Name = "ResolveTest" };
        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Test",
            Description = "Desc",
            MetadataJson = metadata.ToJson(),
            ContentPath = "/tmp/test"
        });

        var key = await _service.GenerateShareKeyAsync(id);
        var resolved = await _service.ResolveShareKeyAsync(key);

        resolved.Name.Should().Be("ResolveTest");
    }

    [Fact]
    public async Task ResolveShareKey_InvalidKey_ThrowsShareKeyParseException()
    {
        var act = () => _service.ResolveShareKeyAsync("sshare+invalid");
        await act.Should().ThrowAsync<ShareKeyParseException>();
    }

    [Fact]
    public async Task ResolveShareKey_WrongPassword_ThrowsShareKeyCryptoException()
    {
        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Test",
            Description = "Desc",
            MetadataJson = "{}",
            ContentPath = "/tmp/test"
        });

        var key = await _service.GenerateShareKeyAsync(id, "correct");
        var act = () => _service.ResolveShareKeyAsync(key, "wrong");
        await act.Should().ThrowAsync<ShareKeyCryptoException>();
    }

    [Fact]
    public async Task ResolveShareKey_NonexistentItem_ThrowsFileGroupNotFoundException()
    {
        var key = _crypto.GenerateShareKey(99999);
        var act = () => _service.ResolveShareKeyAsync(key);
        await act.Should().ThrowAsync<FileGroupNotFoundException>();
    }
}
