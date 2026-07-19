using SteamShare.Core.Tasks;

namespace SteamShare.Test.Tasks;

/// <summary>
/// Unit tests for <see cref="TaskContext"/> — verifies ambient task context
/// enter/exit, nesting, and async propagation via <see cref="AsyncLocal{T}"/>.
/// </summary>
public class TaskContextTests
{
    [Fact]
    public void Enter_SetsCurrentTaskContext()
    {
        var task = new SteamTask { Id = "task-1", Description = "Test task" };

        using (TaskContext.Enter(task))
        {
            TaskContext.Current.Should().NotBeNull();
            TaskContext.Current!.Task.Should().BeSameAs(task);
        }
    }

    [Fact]
    public void Dispose_ClearsCurrentTaskContext()
    {
        var task = new SteamTask { Id = "task-1" };

        using (TaskContext.Enter(task))
        {
            TaskContext.Current.Should().NotBeNull();
        }

        TaskContext.Current.Should().BeNull();
    }

    [Fact]
    public void NestedContexts_RestoreParentOnDispose()
    {
        var parentTask = new SteamTask { Id = "parent" };
        var childTask = new SteamTask { Id = "child" };

        using (TaskContext.Enter(parentTask))
        {
            TaskContext.Current!.Task.Id.Should().Be("parent");

            using (TaskContext.Enter(childTask))
            {
                TaskContext.Current!.Task.Id.Should().Be("child");
            }

            // After inner scope exits, parent should be restored.
            TaskContext.Current!.Task.Id.Should().Be("parent");
        }

        // After outer scope exits, no context remains.
        TaskContext.Current.Should().BeNull();
    }

    [Fact]
    public async Task AsyncMethod_SeesCurrentFromCaller()
    {
        var task = new SteamTask { Id = "async-task" };

        using (TaskContext.Enter(task))
        {
            var result = await GetCurrentTaskIdAsync();
            result.Should().Be("async-task");
        }
    }

    [Fact]
    public async Task AsyncMethod_AfterYield_StillSeesContext()
    {
        var task = new SteamTask { Id = "yield-task" };

        using (TaskContext.Enter(task))
        {
            await Task.Yield(); // Force async continuation on a different thread
            var id = TaskContext.Current?.Task.Id;
            id.Should().Be("yield-task");
        }
    }

    /// <summary>
    /// Simulates awaiting an async operation that reads the ambient context.
    /// </summary>
    private static async Task<string> GetCurrentTaskIdAsync()
    {
        await Task.Yield(); // Force async continuation
        return TaskContext.Current?.Task.Id ?? string.Empty;
    }
}
