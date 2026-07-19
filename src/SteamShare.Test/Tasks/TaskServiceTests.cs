using SteamShare.Core.Tasks;

using TaskStatus = SteamShare.Core.Tasks.TaskStatus;

namespace SteamShare.Test.Tasks;

/// <summary>
/// Unit tests for <see cref="TaskService"/> — verifies all <see cref="ITaskService"/>
/// operations including task tree management, progress reporting, lifecycle transitions,
/// cancellation, auto-completion, and thread safety.
/// </summary>
public class TaskServiceTests
{
    private static TaskService CreateSut() => new();

    // ── Test 1: visible root task appears in GetVisibleRootTasks ──────────

    [Fact]
    public void GetVisibleRootTasks_after_starting_visible_root_task_contains_it()
    {
        var sut = CreateSut();

        using (sut.StartTask(TaskCategory.General, "test task", isVisible: true))
        {
            var visible = sut.GetVisibleRootTasks();

            visible.Should().ContainSingle()
                .Which.Description.Should().Be("test task");
        }
    }

    // ── Test 2: invisible root task excluded from GetVisibleRootTasks ──────

    [Fact]
    public void GetVisibleRootTasks_after_starting_invisible_root_task_excludes_it()
    {
        var sut = CreateSut();

        string taskId;
        using (var scope = sut.StartTask(TaskCategory.General, "hidden", isVisible: false))
        {
            sut.GetVisibleRootTasks().Should().BeEmpty();

            // The task should still be findable by ID.
            var visible = sut.GetVisibleRootTasks();
            visible.Should().BeEmpty();

            // Grab the task from the internal store for later verification.
            taskId = ((TaskService.TaskScopeProxy)scope)._task.Id;
        }

        // After disposal, invisible task should still be findable via GetTask.
        sut.GetTask(taskId).Should().NotBeNull();
    }

    // ── Test 3: child via TaskContext.Enter has parent; root still visible ─

    [Fact]
    public void StartTask_with_ambient_task_creates_child_and_root_stays_visible()
    {
        var sut = CreateSut();
        var parentTask = new SteamTask { Id = "parent-1", Description = "parent", IsVisible = true };

        string childId;
        using (TaskContext.Enter(parentTask))
        {
            using (var childScope = sut.StartTask(TaskCategory.Upload, "child task", isVisible: false))
            {
                childId = ((TaskService.TaskScopeProxy)childScope)._task.Id;

                // Child should have parent.
                var child = sut.GetTask(childId);
                child.Should().NotBeNull();
                child!.ParentTaskId.Should().Be("parent-1");
            }
        }
    }

    // ── Test 4: ReportProgress updates ambient task and fires OnTaskChanged ─

    [Fact]
    public void ReportProgress_updates_ambient_task_and_fires_OnTaskChanged()
    {
        var sut = CreateSut();
        var captures = new List<(double progress, string? text)>();
        sut.OnTaskChanged += t => captures.Add((t.Progress, t.ProgressText));

        using (sut.StartTask(TaskCategory.Download, "download file"))
        {
            sut.ReportProgress(50, "half done");
        }

        captures.Should().Contain(c => c.progress == 50 && c.text == "half done");
    }

    // ── Test 5: Fail sets status, end time, exception, fires OnTaskChanged ─

    [Fact]
    public void Fail_sets_status_endtime_exception_and_fires_OnTaskChanged()
    {
        var sut = CreateSut();
        var events = new List<SteamTask>();
        sut.OnTaskChanged += events.Add;

        using (sut.StartTask(TaskCategory.Delete, "delete item"))
        {
            sut.Fail(new InvalidOperationException("boom"));
        }

        events.Should().NotBeEmpty();
        var task = events[^1]; // last event is from Fail
        task.Status.Should().Be(TaskStatus.Failed);
        task.EndTime.Should().NotBeNull();
        task.LastException.Should().Be(new InvalidOperationException("boom").ToString());
    }

    // ── Test 6: Complete sets status, end time, progress=100, fires OnTaskChanged ─

    [Fact]
    public void Complete_sets_status_endtime_progress_and_fires_OnTaskChanged()
    {
        var sut = CreateSut();
        var events = new List<SteamTask>();
        sut.OnTaskChanged += events.Add;

        using (sut.StartTask(TaskCategory.Share, "share file"))
        {
            sut.Complete();
        }

        events.Should().NotBeEmpty();
        var task = events[^1];
        task.Status.Should().Be(TaskStatus.Completed);
        task.EndTime.Should().NotBeNull();
        task.Progress.Should().Be(100);
    }

    // ── Test 7: CancellationToken cancels task ──────────────────────────────

    [Fact]
    public void CancellationToken_cancels_task()
    {
        var sut = CreateSut();
        SteamTask? captured = null;
        sut.OnTaskChanged += t => captured = t;

        var cts = new CancellationTokenSource();
        using (sut.StartTask(TaskCategory.General, "cancellable", ct: cts.Token))
        {
            cts.Cancel();
        }

        captured.Should().NotBeNull();
        captured!.Status.Should().Be(TaskStatus.Cancelled);
        captured.EndTime.Should().NotBeNull();
    }

    // ── Test 8: TaskScope.Dispose auto-completes if still Running ──────────

    [Fact]
    public void TaskScope_Dispose_auto_completes_if_still_Running()
    {
        var sut = CreateSut();
        SteamTask? task = null;

        using (var scope = sut.StartTask(TaskCategory.General, "auto complete"))
        {
            task = ((TaskService.TaskScopeProxy)scope)._task;
            // Do NOT call Complete() or Fail() — scope dispose should handle it.
        }

        task!.Status.Should().Be(TaskStatus.Completed);
        task.Progress.Should().Be(100);
        task.EndTime.Should().NotBeNull();
    }

    // ── Test 9: nested tasks — parent progress aggregates children ──────────

    [Fact]
    public void Nested_tasks_parent_progress_aggregates_children()
    {
        // Test the aggregation algorithm used by TaskService.ReportProgress
        // when propagating child progress up to the parent.
        // SteamTask.AggregateProgress() averages all children's progress
        // and is called by ReportProgress to update the parent.
        var parentTask = new SteamTask { Id = "p-agg", Description = "parent agg", IsVisible = true };
        var child1 = new SteamTask { Progress = 40 };
        var child2 = new SteamTask { Progress = 60 };
        parentTask.AddChild(child1);
        parentTask.AddChild(child2);

        parentTask.Children.Should().HaveCount(2);
        var aggregated = parentTask.AggregateProgress();
        aggregated.Should().BeApproximately(50, 0.01);
    }

    // ── Test 10: concurrent StartTask calls don't corrupt state ────────────

    [Fact]
    public void Concurrent_StartTask_calls_do_not_corrupt_state()
    {
        var sut = CreateSut();
        const int count = 100;
        var scopes = new System.Collections.Concurrent.ConcurrentBag<IDisposable>();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // Fire many concurrent StartTask calls across multiple threads.
        // AsyncLocal may flow across some threads in certain runtimes,
        // so we don't assert exact root-task counts — we verify that the
        // service handles concurrency without corruption or exceptions.
        Parallel.For(0, count, i =>
        {
            try
            {
                var scope = sut.StartTask(
                    TaskCategory.General,
                    $"task {i}",
                    isVisible: i % 2 == 0);
                scopes.Add(scope);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        exceptions.Should().BeEmpty("no exceptions should be thrown during concurrent access");

        // GetVisibleRootTasks and GetTask must not throw and must return valid data.
        var visible = sut.GetVisibleRootTasks();
        visible.Should().NotBeNull();
        visible.Should().OnlyContain(t => t.IsVisible);

        // Clean up all scopes.
        foreach (var scope in scopes)
        {
            scope.Dispose();
        }
    }

    // ── Test 11: Nested tasks — parent progress aggregates to 50 then 100 ──

    [Fact]
    public void Nested_tasks_parent_progress_aggregates_to_50_then_100()
    {
        var sut = CreateSut();

        using (var parentScope = sut.StartTask(TaskCategory.General, "parent"))
        {
            var parentTask = ((TaskService.TaskScopeProxy)parentScope)._task;

            // Start and complete child 1 (progress → 100 via Complete).
            using (sut.StartTask(TaskCategory.Upload, "child1"))
            {
                sut.ReportProgress(50);
                sut.Complete();
            }

            // Start child 2 (Progress=0 initially).
            using (var child2Scope = sut.StartTask(TaskCategory.Upload, "child2"))
            {
                // Parent has 2 children: child1(100) + child2(0) → avg = 50.
                parentTask.AggregateProgress().Should().BeApproximately(50, 0.01);

                sut.ReportProgress(100);
                sut.Complete();

                // Now: child1(100) + child2(100) → avg = 100.
                parentTask.AggregateProgress().Should().BeApproximately(100, 0.01);
            }
        }
    }

    // ── Test 12: Invisible child not in visible roots but in parent.Children ──

    [Fact]
    public void Invisible_child_not_in_visible_roots_but_in_parent_children()
    {
        var sut = CreateSut();

        using (var parentScope = sut.StartTask(TaskCategory.General, "parent", isVisible: true))
        {
            var parentTask = ((TaskService.TaskScopeProxy)parentScope)._task;

            using (var childScope = sut.StartTask(TaskCategory.Upload, "child", isVisible: false))
            {
                var childTask = ((TaskService.TaskScopeProxy)childScope)._task;

                // Child must NOT appear in GetVisibleRootTasks.
                sut.GetVisibleRootTasks().Should().NotContain(t => t.Id == childTask.Id);

                // Child IS in the parent's Children collection.
                parentTask.Children.Should().Contain(t => t.Id == childTask.Id);
            }
        }
    }

    // ── Test 13: ReportProgress boundary values 0 and 100 ──

    [Fact]
    public void ReportProgress_boundary_zero_and_hundred()
    {
        var sut = CreateSut();

        using (var scope = sut.StartTask(TaskCategory.General, "boundary"))
        {
            var task = ((TaskService.TaskScopeProxy)scope)._task;

            sut.ReportProgress(0);
            task.Progress.Should().Be(0);

            sut.ReportProgress(100);
            task.Progress.Should().Be(100);
        }
    }

    // ── Test 14: Fail sets LastException with type name and message ──

    [Fact]
    public void Fail_sets_LastException_with_exception_type_and_message()
    {
        var sut = CreateSut();

        using (var scope = sut.StartTask(TaskCategory.General, "fail test"))
        {
            var task = ((TaskService.TaskScopeProxy)scope)._task;

            sut.Fail(new InvalidOperationException("test failure reason"));

            task.LastException.Should().Contain("InvalidOperationException");
            task.LastException.Should().Contain("test failure reason");
        }
    }

    // ── Test 15: Complete on already-completed task is no-op ──

    [Fact]
    public void Complete_on_already_completed_task_is_noop()
    {
        var sut = CreateSut();

        using (var scope = sut.StartTask(TaskCategory.General, "complete twice"))
        {
            var task = ((TaskService.TaskScopeProxy)scope)._task;

            sut.Complete();
            var firstEndTime = task.EndTime!.Value;
            task.Status.Should().Be(TaskStatus.Completed);

            // Second Complete must not throw and must preserve state.
            var act = () => sut.Complete();
            act.Should().NotThrow();
            task.Status.Should().Be(TaskStatus.Completed);
            task.EndTime.Should().BeCloseTo(firstEndTime, TimeSpan.FromMilliseconds(100));
        }
    }

    // ── Test 16: CancellationToken fires, sets Cancelled and EndTime ──

    [Fact]
    public void CancellationToken_fires_sets_cancelled_and_endtime()
    {
        var sut = CreateSut();
        SteamTask? cancelledTask = null;
        sut.OnTaskChanged += t =>
        {
            if (t.Status == TaskStatus.Cancelled)
            {
                cancelledTask = t;
            }
        };

        var cts = new CancellationTokenSource();
        using (var scope = sut.StartTask(TaskCategory.General, "cancel", ct: cts.Token))
        {
            var task = ((TaskService.TaskScopeProxy)scope)._task;
            cts.Cancel();

            cancelledTask.Should().NotBeNull();
            cancelledTask!.Id.Should().Be(task.Id);
            cancelledTask.Status.Should().Be(TaskStatus.Cancelled);
            cancelledTask.EndTime.Should().NotBeNull();
        }
    }

    // ── Test 17: TaskContext 3-level nesting restores correctly ──

    [Fact]
    public void TaskContext_three_level_nesting_restores_correctly()
    {
        var taskA = new SteamTask { Id = "A", Description = "level A" };
        var taskB = new SteamTask { Id = "B", Description = "level B" };
        var taskC = new SteamTask { Id = "C", Description = "level C" };

        using (TaskContext.Enter(taskA))
        {
            TaskContext.Current!.Task.Id.Should().Be("A");

            using (TaskContext.Enter(taskB))
            {
                TaskContext.Current!.Task.Id.Should().Be("B");

                using (TaskContext.Enter(taskC))
                {
                    TaskContext.Current!.Task.Id.Should().Be("C");
                }

                // Dispose C → restore B.
                TaskContext.Current!.Task.Id.Should().Be("B");
            }

            // Dispose B → restore A.
            TaskContext.Current!.Task.Id.Should().Be("A");
        }

        // Dispose A → restore null.
        TaskContext.Current.Should().BeNull();
    }

    // ── Test 18: Concurrent StartTask — 10 parallel, all findable ──

    [Fact]
    public void Concurrent_StartTask_ten_parallel_all_tasks_exist()
    {
        var sut = CreateSut();
        const int count = 10;
        var taskIds = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, count, i =>
        {
            try
            {
                using (var scope = sut.StartTask(TaskCategory.General, $"concurrent {i}"))
                {
                    var task = ((TaskService.TaskScopeProxy)scope)._task;
                    taskIds.TryAdd(task.Id, true);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        exceptions.Should().BeEmpty();
        taskIds.Should().HaveCount(count);

        // All tasks must be retrievable via GetTask.
        foreach (var id in taskIds.Keys)
        {
            sut.GetTask(id).Should().NotBeNull($"task {id} should exist in _allTasks");
        }
    }

    // ── Test 19: ReportProgress without ambient task is no-op ──

    [Fact]
    public void ReportProgress_without_ambient_task_does_not_throw()
    {
        var sut = CreateSut();

        var act = () => sut.ReportProgress(42, "no context");
        act.Should().NotThrow();
    }

    // ── Test 20: GetTask with invalid ID returns null ──

    [Fact]
    public void GetTask_with_invalid_id_returns_null()
    {
        var sut = CreateSut();

        var result = sut.GetTask("nonexistent-id-12345");
        result.Should().BeNull();
    }

    // ── Test 21: TaskScope.Dispose when already Failed preserves status ──

    [Fact]
    public void TaskScope_Dispose_when_already_failed_preserves_failed_status()
    {
        var sut = CreateSut();
        SteamTask? task = null;

        using (var scope = sut.StartTask(TaskCategory.General, "fail then dispose"))
        {
            task = ((TaskService.TaskScopeProxy)scope)._task;
            sut.Fail(new Exception("forced failure"));
            task.Status.Should().Be(TaskStatus.Failed);
        }

        // After Dispose, the task must still be Failed, not Completed.
        task!.Status.Should().Be(TaskStatus.Failed);
        task.LastException.Should().NotBeNull();
    }

    // ── Test 22: AggregateProgress with no children returns own Progress ──

    [Fact]
    public void AggregateProgress_with_no_children_returns_own_progress()
    {
        var task = new SteamTask { Progress = 75 };
        task.AggregateProgress().Should().Be(75);

        task.Progress = 0;
        task.AggregateProgress().Should().Be(0);

        task.Progress = 100;
        task.AggregateProgress().Should().Be(100);
    }

    // ── Test 23: AggregateProgress with three children averages all three ──

    [Fact]
    public void AggregateProgress_with_three_children_averages_all_three()
    {
        var parent = new SteamTask { Id = "p", Description = "parent" };
        parent.AddChild(new SteamTask { Progress = 30 });
        parent.AddChild(new SteamTask { Progress = 60 });
        parent.AddChild(new SteamTask { Progress = 90 });

        parent.AggregateProgress().Should().BeApproximately(60, 0.01);
    }

    // ── Test 24: Thread safety — GetVisibleRootTasks under concurrency ──

    [Fact]
    public void GetVisibleRootTasks_protected_by_lock_does_not_corrupt_under_concurrency()
    {
        var sut = CreateSut();
        var scopes = new System.Collections.Concurrent.ConcurrentBag<IDisposable>();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // Start several root tasks.
        for (int i = 0; i < 10; i++)
        {
            var scope = sut.StartTask(TaskCategory.General, $"root {i}", isVisible: true);
            scopes.Add(scope);
        }

        // Concurrently read GetVisibleRootTasks from multiple threads.
        Parallel.For(0, 50, _ =>
        {
            try
            {
                var visible = sut.GetVisibleRootTasks();
                visible.Should().NotBeNull();
                visible.Should().OnlyContain(t => t.IsVisible);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        exceptions.Should().BeEmpty();

        // Cleanup.
        foreach (var scope in scopes)
        {
            scope.Dispose();
        }
    }

    // ── Test 25: OnTaskChanged fires for every status and progress change ──

    [Fact]
    public void OnTaskChanged_fires_for_every_status_and_progress_change()
    {
        var sut = CreateSut();
        var events = new List<(TaskStatus status, double progress)>();
        sut.OnTaskChanged += t => events.Add((t.Status, t.Progress));

        using (var scope = sut.StartTask(TaskCategory.General, "event test"))
        {
            sut.ReportProgress(25);
            sut.ReportProgress(50);
            sut.ReportProgress(75);
            sut.Complete();
        }

        // Events should include progress changes and final completion.
        events.Should().Contain(e => e.status == TaskStatus.Running && e.progress == 25);
        events.Should().Contain(e => e.status == TaskStatus.Running && e.progress == 50);
        events.Should().Contain(e => e.status == TaskStatus.Running && e.progress == 75);
        events.Should().Contain(e => e.status == TaskStatus.Completed && e.progress == 100);
    }
}
