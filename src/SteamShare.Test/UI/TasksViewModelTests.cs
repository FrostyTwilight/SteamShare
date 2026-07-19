using System.ComponentModel;

using SteamShare.Core.Localization;
using SteamShare.Core.Models;
using SteamShare.Core.Services;
using SteamShare.Core.Tasks;
using SteamShare.UI.Services;
using SteamShare.UI.ViewModels;

using DummyWorkshopService = SteamShare.Test.Dummy.DummyWorkshopService;
using TaskStatus = SteamShare.Core.Tasks.TaskStatus;

namespace SteamShare.Test.UI;

/// <summary>
/// Unit tests for <see cref="TasksViewModel"/> and <see cref="TaskItemViewModel"/>
/// using NSubstitute mocks for all injected dependencies.
/// </summary>
public class TasksViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public TasksViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SteamShare_TasksVM", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // When running tests in parallel, another test collection may initialize
        // the static Dispatcher.UIThread on a different thread, causing
        // CheckAccess() to return false and Refresh() to post (never executed).
        // Force the dispatcher onto this thread before any ViewModel is created.
        ResetAvaloniaDispatcher();
    }

    private static void ResetAvaloniaDispatcher()
    {
        var field = typeof(Avalonia.Threading.Dispatcher).GetField(
            "s_uiThread",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        field?.SetValue(null, null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="TasksViewModel"/> with fully mocked dependencies.
    /// Use <paramref name="configureTaskService"/> to set up task service
    /// behavior BEFORE the ViewModel constructor calls Refresh().
    /// </summary>
    private (TasksViewModel Sut,
        ITaskService TaskService,
        INotificationService NotificationService) CreateSut(
            Action<ITaskService>? configureTaskService = null)
    {
        var taskService = Substitute.For<ITaskService>();
        configureTaskService?.Invoke(taskService);

        var cryptoService = Substitute.For<IShareKeyCryptoService>();
        var configService = Substitute.For<IConfigService>();
        var notificationService = Substitute.For<INotificationService>();
        var dialogService = Substitute.For<IDialogService>();
        var loc = new LocalizationService();
        var workshop = Substitute.For<IWorkshopService>();

        configService.Current.Returns(new AppConfig());

        // Use real instances instead of NSubstitute proxies — TrackingDatabaseService,
        // ShareService, and DownloadOrchestrator are sealed and cannot be mocked.
        var trackingDb = new TrackingDatabaseService(_tempDir);
        var fileGroupManager = new FileGroupManager(workshop, trackingDb);
        var shareService = new ShareService(cryptoService, workshop);
        var downloadOrch = new DownloadOrchestrator(
            workshop, fileGroupManager, trackingDb, shareService,
            taskService, configService, cryptoService);

        var sut = new TasksViewModel(
            taskService,
            cryptoService,
            shareService: null!,
            fileGroupManager: null!,
            configService,
            notificationService,
            dialogService,
            loc,
            workshop,
            downloadOrch);

        return (sut, taskService, notificationService);
    }

    /// <summary>
    /// Creates a <see cref="TasksViewModel"/> with real <see cref="ShareService"/>
    /// and <see cref="FileGroupManager"/> backed by <see cref="DummyWorkshopService"/>,
    /// plus a mocked <see cref="ITaskService"/>.
    /// Suitable for testing the download flow.
    /// </summary>
    private static async Task<(TasksViewModel Sut,
        ITaskService TaskService,
        DummyWorkshopService Workshop,
        string TempDownloadDir)> CreateSutWithRealServicesAsync(string tempDir)
    {
        var workshop = new DummyWorkshopService();
        await workshop.InitializeAsync();

        var crypto = new ShareKeyCryptoService();
        var shareService = new ShareService(crypto, workshop);

        var tempDownloadDir = Path.Combine(tempDir, "downloads");
        Directory.CreateDirectory(tempDownloadDir);

        var trackingDbDir = Path.Combine(tempDir, "tracking");
        Directory.CreateDirectory(trackingDbDir);
        var trackingDb = new TrackingDatabaseService(trackingDbDir);
        var fileGroupManager = new FileGroupManager(workshop, trackingDb);

        var taskService = Substitute.For<ITaskService>();
        var configService = Substitute.For<IConfigService>();
        var notificationService = Substitute.For<INotificationService>();
        var dialogService = Substitute.For<IDialogService>();
        var loc = new LocalizationService();

        configService.Current.Returns(new AppConfig { DownloadDirectory = tempDownloadDir });

        var downloadOrch = new DownloadOrchestrator(workshop, fileGroupManager, trackingDb, shareService, taskService, configService, crypto);

        var sut = new TasksViewModel(
            taskService,
            crypto,
            shareService,
            fileGroupManager,
            configService,
            notificationService,
            dialogService,
            loc,
            workshop,
            downloadOrch);

        return (sut, taskService, workshop, tempDownloadDir);
    }

    // ── Test 1: RefreshCommand populates ActiveTasks ────────────────────────

    [Fact]
    public void RefreshCommand_WhenServiceReturnsTwoActiveTasks_PopulatesActiveTasks()
    {
        var tasks = new List<SteamTask>
        {
            new() { Description = "Download A", Status = TaskStatus.Running },
            new() { Description = "Upload B", Status = TaskStatus.Pending },
        };

        var (sut, taskService, _) = CreateSut(ts => ts.GetVisibleRootTasks().Returns(tasks));

        sut.RefreshCommand.Execute(null);

        sut.ActiveTasks.Should().HaveCount(2);
        sut.CompletedTasks.Should().BeEmpty();
    }

    // ── Test 2: RefreshCommand splits Completed ─────────────────────────────

    [Fact]
    public void RefreshCommand_WhenServiceReturnsMixedTasks_SplitsIntoActiveAndCompleted()
    {
        var tasks = new List<SteamTask>
        {
            new() { Description = "Running task", Status = TaskStatus.Running },
            new() { Description = "Done task", Status = TaskStatus.Completed },
        };

        var (sut, taskService, _) = CreateSut(ts => ts.GetVisibleRootTasks().Returns(tasks));

        sut.RefreshCommand.Execute(null);

        sut.ActiveTasks.Should().HaveCount(1);
        sut.ActiveTasks[0].Description.Should().Be("Running task");
        sut.CompletedTasks.Should().HaveCount(1);
        sut.CompletedTasks[0].Description.Should().Be("Done task");
    }

    // ── Test 3: OnTaskChanged triggers refresh ──────────────────────────────

    [Fact]
    public void OnTaskChanged_WhenEventFired_RefreshesCollections()
    {
        var initialTasks = new List<SteamTask>
        {
            new() { Description = "Task A", Status = TaskStatus.Running },
        };

        var (sut, taskService, _) = CreateSut(ts => ts.GetVisibleRootTasks().Returns(initialTasks));
        sut.RefreshCommand.Execute(null);

        // Change the return value to simulate new task state,
        // then invoke the handler directly to trigger a refresh.
        var updatedTasks = new List<SteamTask>
        {
            new() { Description = "Task A", Status = TaskStatus.Completed },
            new() { Description = "Task B", Status = TaskStatus.Pending },
        };
        taskService.GetVisibleRootTasks().Returns(updatedTasks);

        // Invoke the private OnTaskChanged handler directly via reflection
        // to bypass NSubstitute event-raising limitations in the test environment.
        var handlerMethod = typeof(TasksViewModel).GetMethod(
            "OnTaskChanged",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        handlerMethod.Should().NotBeNull();
        handlerMethod!.Invoke(sut, [new SteamTask { Status = TaskStatus.Completed }]);

        sut.ActiveTasks.Should().HaveCount(1);
        sut.ActiveTasks[0].Description.Should().Be("Task B");
        sut.CompletedTasks.Should().HaveCount(1);
        sut.CompletedTasks[0].Description.Should().Be("Task A");
    }

    // ── Test 4: StartDownloadCommand creates visible task ───────────────────

    [Fact]
    public async Task StartDownloadCommand_WithValidShareKey_CreatesVisibleDownloadTask()
    {
        var (sut, taskService, workshop, tempDownloadDir) =
            await CreateSutWithRealServicesAsync(_tempDir);

        // Create a workshop item with metadata and a dummy content file.
        var dummyZipPath = Path.Combine(_tempDir, "dummy.zip");
        await File.WriteAllTextAsync(dummyZipPath, "test content for download");

        var metadata = new FileGroupMetadata { Name = "TestDownload" };
        var id = await workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "TestDownload",
            Description = "A test download",
            MetadataJson = metadata.ToJson(),
            ContentPath = dummyZipPath,
        });

        var crypto = new ShareKeyCryptoService();
        var shareKey = crypto.GenerateShareKey(id);

        // Mock ITaskService.StartTask to return a no-op scope
        var mockScope = Substitute.For<ITaskScope>();
        taskService.StartTask(
                Arg.Any<TaskCategory>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(mockScope);

        sut.ShareKey = shareKey;

        await sut.StartDownloadCommand.ExecuteAsync(null);

        taskService.Received().StartTask(
            TaskCategory.Download,
            Arg.Is<string>(desc => desc == "TestDownload"),
            Arg.Is<bool>(v => v),
            Arg.Any<CancellationToken>());
    }

    // ── Test 5: TaskItemViewModel wraps SteamTask correctly ─────────────────

    [Fact]
    public void TaskItemViewModel_FromSteamTask_MapsAllProperties()
    {
        var task = new SteamTask
        {
            Id = "abc123",
            Description = "Test task",
            Category = TaskCategory.Upload,
            Status = TaskStatus.Running,
            Progress = 50,
            ProgressText = "Halfway done",
            LastException = null,
        };
        var startTime = task.StartTime;
        task.EndTime = null;
        task.Children.Add(new SteamTask { Description = "Child subtask", Progress = 100 });

        var vm = new TaskItemViewModel(task);

        vm.Id.Should().Be("abc123");
        vm.Description.Should().Be("Test task");
        vm.Category.Should().Be(TaskCategory.Upload);
        vm.Status.Should().Be(TaskStatus.Running);
        vm.Progress.Should().Be(50);
        vm.ProgressText.Should().Be("Halfway done");
        vm.StartTime.Should().Be(startTime);
        vm.EndTime.Should().BeNull();
        vm.HasError.Should().BeFalse();
        vm.LastException.Should().BeNull();
        vm.Children.Should().HaveCount(1);
        vm.Children[0].Description.Should().Be("Child subtask");
    }

    // ── Test 6: CancelCommand calls CTS.Cancel() ────────────────────────────

    [Fact]
    public void CancelCommand_WhenCtsExists_CancelsToken()
    {
        var task = new SteamTask { Description = "Cancellable" };
        var vm = new TaskItemViewModel(task);
        var cts = new CancellationTokenSource();
        vm.Cts = cts;

        vm.CancelCommand.Execute(null);

        cts.IsCancellationRequested.Should().BeTrue();
    }

    // ── Test 7: IsLoading tracks operation ──────────────────────────────────

    [Fact]
    public void IsLoading_WhenSet_NotifiesPropertyChanged()
    {
        var (sut, _, _) = CreateSut();
        var propertyNames = new List<string?>();
        sut.PropertyChanged += (_, e) => propertyNames.Add(e.PropertyName);

        sut.IsLoading = true;

        propertyNames.Should().Contain(nameof(TasksViewModel.IsLoading));
    }

    // ── Test 8: Empty ShareKey shows warning ────────────────────────────────

    [Fact]
    public async Task StartDownloadCommand_WithWhitespaceShareKey_ShowsWarning()
    {
        var (sut, taskService, notificationService) = CreateSut();

        sut.ShareKey = "   ";

        await sut.StartDownloadCommand.ExecuteAsync(null);

        notificationService.Received().ShowWarning(Arg.Any<string>());
        taskService.DidNotReceive().StartTask(
            Arg.Any<TaskCategory>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }
}
