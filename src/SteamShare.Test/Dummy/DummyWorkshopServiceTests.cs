using SteamShare.Core.Services;

namespace SteamShare.Test.Dummy;

public class DummyWorkshopServiceTests
{
    [Fact]
    public async Task Initialize_ReturnsTrue()
    {
        var service = new DummyWorkshopService();
        var result = await service.InitializeAsync();
        result.Should().BeTrue();
        service.IsSteamRunning.Should().BeTrue();
    }

    [Fact]
    public async Task CreateItem_ReturnsIncreasingIds()
    {
        var service = new DummyWorkshopService();
        await service.InitializeAsync();

        var id1 = await service.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Test",
            Description = "Desc",
            MetadataJson = "{}",
            ContentPath = "/tmp/test"
        });
        var id2 = await service.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Test2",
            Description = "Desc",
            MetadataJson = "{}",
            ContentPath = "/tmp/test"
        });

        id2.Should().BeGreaterThan(id1);
    }

    [Fact]
    public async Task CreateWithSampleData_PopulatesItems()
    {
        var service = DummyWorkshopService.CreateWithSampleData(5);
        var items = await service.QueryOwnedItemsAsync();
        items.Should().HaveCount(5);
    }

    [Fact]
    public async Task QueryItemById_ReturnsCorrectItem()
    {
        var service = DummyWorkshopService.CreateWithSampleData(3);
        var item = await service.QueryItemByIdAsync(1);
        item.Should().NotBeNull();
        item!.Title.Should().Be("Test File Group 1");
    }
}
