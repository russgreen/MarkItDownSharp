using System.Text;
using MarkItDownSharp.Presentation;
using MarkItDownSharp.Worker;

namespace MarkItDownSharp.Tests;

public sealed class PresentationTests
{
    private static TimeSpan Timeout => TimeSpan.FromSeconds(30);

    // ---- Start, convert, restart ----

    [Fact]
    public async Task EndToEnd_ConvertsFilesAndRemembersThemAfterARestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var env = await TestEnvironment.CreateAsync(cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(env.Input, "notes.txt"), "hello from the smoke test", cancellationToken);
        await env.SaveSettingsAsync(s => s.ProcessExistingFiles = true);

        await using (var first = env.CreateViewModel())
        {
            await first.LoadAsync();
            Assert.True(first.ShowWelcome);
            Assert.False(first.HasValidationErrors);
            await first.StartCommand.ExecuteAsync(null);
            Assert.True(first.IsRunning);
            Assert.False(first.ShowWelcome);

            await env.WaitUntilAsync(() => first.Jobs.Any(j => j.State == ConversionJobState.Completed), cancellationToken);

            var job = Assert.Single(first.Jobs);
            Assert.Equal("Completed", job.StateText);
            Assert.Equal("hello from the smoke test", await File.ReadAllTextAsync(Path.Combine(env.Output, "notes.md"), cancellationToken));
            await env.StopAsync(first);
            Assert.False(first.IsRunning);
        }

        // A new application instance sees the same history and settings.
        await using var second = env.CreateViewModel();
        await second.LoadAsync();

        var restored = Assert.Single(second.Jobs);
        Assert.Equal(ConversionJobState.Completed, restored.State);
        Assert.Equal(Path.Combine(env.Input, "notes.txt"), restored.SourcePath);
        Assert.Equal(env.Input, second.SourceDirectory);
        Assert.False(second.ShowWelcome);
    }

    [Fact]
    public async Task FailedJobs_AreRememberedAndCanBeRetriedAfterARestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var env = await TestEnvironment.CreateAsync(cancellationToken);
        var source = Path.Combine(env.Input, "broken.ipynb");
        await File.WriteAllTextAsync(source, "this is not a notebook", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(env.Input, "skip.txt"), "x", cancellationToken);
        await env.SaveSettingsAsync(s =>
        {
            s.ProcessExistingFiles = true;
            s.CollisionBehavior = CollisionBehavior.Skip;
        });
        Directory.CreateDirectory(env.Output);
        await File.WriteAllTextAsync(Path.Combine(env.Output, "skip.md"), "already here", cancellationToken);

        await using (var first = env.CreateViewModel())
        {
            await first.LoadAsync();
            await first.StartCommand.ExecuteAsync(null);
            await env.WaitUntilAsync(() => first.Jobs.Count == 2 && first.Jobs.All(j => j.IsFinished), cancellationToken);

            Assert.Equal(ConversionJobState.Failed, first.Jobs.Single(j => j.FileName == "broken.ipynb").State);
            Assert.Equal(ConversionJobState.Skipped, first.Jobs.Single(j => j.FileName == "skip.txt").State);
            Assert.Equal(1, first.FailedJobCount);
            await env.StopAsync(first);
        }

        // After a restart the failed and skipped jobs are still listed; retry needs a running monitor.
        await File.WriteAllTextAsync(source, """{"cells":[{"cell_type":"markdown","source":"# Fixed"}]}""", cancellationToken);
        await env.SaveSettingsAsync(s => s.ProcessExistingFiles = false);
        await using var second = env.CreateViewModel();
        await second.LoadAsync();
        var failed = second.Jobs.Single(j => j.FileName == "broken.ipynb");
        Assert.Equal(ConversionJobState.Failed, failed.State);
        Assert.False(string.IsNullOrWhiteSpace(failed.Error));
        Assert.False(second.RetryCommand.CanExecute(failed));

        await second.StartCommand.ExecuteAsync(null);
        Assert.True(second.RetryCommand.CanExecute(failed));
        await second.RetryCommand.ExecuteAsync(failed);
        await env.WaitUntilAsync(() => failed.State == ConversionJobState.Completed, cancellationToken);

        Assert.Equal("# Fixed", await File.ReadAllTextAsync(Path.Combine(env.Output, "broken.md"), cancellationToken));
        Assert.Null(failed.Error);
        await env.StopAsync(second);
    }

    [Fact]
    public async Task RetryFailed_RunsEveryFailedJobAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var env = await TestEnvironment.CreateAsync(cancellationToken);
        var paths = new[] { "a.ipynb", "b.ipynb" }.Select(n => Path.Combine(env.Input, n)).ToArray();
        foreach (var path in paths)
        {
            await File.WriteAllTextAsync(path, "broken", cancellationToken);
        }

        await env.SaveSettingsAsync(s => s.ProcessExistingFiles = true);
        await using var vm = env.CreateViewModel();
        await vm.LoadAsync();
        await vm.StartCommand.ExecuteAsync(null);
        await env.WaitUntilAsync(() => vm.Jobs.Count == 2 && vm.Jobs.All(j => j.State == ConversionJobState.Failed), cancellationToken);
        foreach (var path in paths)
        {
            await File.WriteAllTextAsync(path, """{"cells":[]}""", cancellationToken);
        }

        await vm.RetryFailedCommand.ExecuteAsync(null);
        await env.WaitUntilAsync(() => vm.Jobs.All(j => j.State == ConversionJobState.Completed), cancellationToken);

        Assert.Equal(0, vm.FailedJobCount);
    }

    // ---- Validation ----

    [Fact]
    public async Task Validation_BlocksStartAndExplainsTheProblem()
    {
        var env = await TestEnvironment.CreateAsync(TestContext.Current.CancellationToken);
        await using var vm = env.CreateViewModel();
        await vm.LoadAsync();

        vm.SourceDirectory = Path.Combine(env.Root, "missing");
        vm.OutputDirectory = env.Output;

        Assert.True(vm.HasValidationErrors);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.Contains(vm.ValidationIssues, i => i.Field == MonitorSettingsValidator.InputField && i.Message.Contains("does not exist"));

        vm.SourceDirectory = env.Input;

        Assert.False(vm.HasValidationErrors);
        Assert.True(vm.StartCommand.CanExecute(null));
    }

    // ---- Pause, cancel, clear ----

    [Fact]
    public async Task PauseHoldsFilesAndResumeConvertsThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var env = await TestEnvironment.CreateAsync(cancellationToken);
        await using var vm = env.CreateViewModel();
        await vm.LoadAsync();
        await vm.StartCommand.ExecuteAsync(null);

        vm.PauseResumeCommand.Execute(null);
        Assert.True(vm.IsPaused);
        Assert.Equal("Resume", vm.PauseResumeLabel);
        await File.WriteAllTextAsync(Path.Combine(env.Input, "held.txt"), "held", cancellationToken);
        await env.DelayAsync(1500, cancellationToken);
        Assert.DoesNotContain(vm.Jobs, j => j.State == ConversionJobState.Completed);

        vm.PauseResumeCommand.Execute(null);
        await env.WaitUntilAsync(() => vm.Jobs.Any(j => j.State == ConversionJobState.Completed), cancellationToken);

        Assert.False(vm.IsPaused);
        await env.StopAsync(vm);
    }

    [Fact]
    public async Task ClearHistory_RemovesFinishedJobsFromListAndDisk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var env = await TestEnvironment.CreateAsync(cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(env.Input, "a.txt"), "a", cancellationToken);
        await env.SaveSettingsAsync(s => s.ProcessExistingFiles = true);
        await using (var vm = env.CreateViewModel())
        {
            await vm.LoadAsync();
            await vm.StartCommand.ExecuteAsync(null);
            await env.WaitUntilAsync(() => vm.Jobs.Any(j => j.State == ConversionJobState.Completed), cancellationToken);
            await env.StopAsync(vm);

            await vm.ClearHistoryCommand.ExecuteAsync(null);

            Assert.Empty(vm.Jobs);
        }

        await using var reloaded = env.CreateViewModel();
        await reloaded.LoadAsync();
        Assert.Empty(reloaded.Jobs);
    }

    [Fact]
    public async Task OpenActions_UseTheShellAndOnlyWhenFilesExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var env = await TestEnvironment.CreateAsync(cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(env.Input, "a.txt"), "a", cancellationToken);
        await env.SaveSettingsAsync(s => s.ProcessExistingFiles = true);
        await using var vm = env.CreateViewModel();
        await vm.LoadAsync();
        await vm.StartCommand.ExecuteAsync(null);
        await env.WaitUntilAsync(() => vm.Jobs.Any(j => j.State == ConversionJobState.Completed), cancellationToken);
        var job = vm.Jobs[0];

        Assert.False(vm.OpenOutputCommand.CanExecute(null));
        vm.SelectedJob = job;
        Assert.True(vm.OpenSourceCommand.CanExecute(null));
        Assert.True(vm.OpenOutputCommand.CanExecute(null));
        vm.OpenSourceCommand.Execute(null);
        vm.OpenOutputCommand.Execute(null);
        vm.ShowOutputInFolderCommand.Execute(null);

        Assert.Equal([job.SourcePath, job.OutputPath], env.Shell.Opened);
        Assert.Equal([job.OutputPath], env.Shell.Revealed);
        await env.StopAsync(vm);
    }

    // ---- Notifications ----

    [Theory]
    [InlineData(JobNotificationMode.None, 0)]
    [InlineData(JobNotificationMode.FailuresOnly, 1)]
    [InlineData(JobNotificationMode.All, 2)]
    public async Task JobNotifications_FollowTheConfiguredMode(JobNotificationMode mode, int expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var env = await TestEnvironment.CreateAsync(cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(env.Input, "good.txt"), "ok", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(env.Input, "bad.ipynb"), "broken", cancellationToken);
        await env.SaveSettingsAsync(s =>
        {
            s.ProcessExistingFiles = true;
            s.JobNotifications = mode;
        });
        await using var vm = env.CreateViewModel();
        await vm.LoadAsync();

        await vm.StartCommand.ExecuteAsync(null);
        await env.WaitUntilAsync(() => vm.Jobs.Count == 2 && vm.Jobs.All(j => j.IsFinished), cancellationToken);
        await env.StopAsync(vm);

        Assert.Equal(expected, env.Shell.Notifications.Count);
        if (mode == JobNotificationMode.FailuresOnly)
        {
            Assert.Contains("bad.ipynb", env.Shell.Notifications[0].Message);
        }
    }

    [Fact]
    public async Task JobNotifications_AreThrottledForLargeBatches()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var env = await TestEnvironment.CreateAsync(cancellationToken);
        for (var i = 0; i < 12; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(env.Input, $"f{i}.txt"), "x", cancellationToken);
        }

        await env.SaveSettingsAsync(s =>
        {
            s.ProcessExistingFiles = true;
            s.JobNotifications = JobNotificationMode.All;
        });
        await using var vm = env.CreateViewModel();
        await vm.LoadAsync();

        await vm.StartCommand.ExecuteAsync(null);
        await env.WaitUntilAsync(() => vm.Jobs.Count == 12 && vm.Jobs.All(j => j.IsFinished), cancellationToken);
        await env.StopAsync(vm);

        Assert.Equal(6, env.Shell.Notifications.Count);
        Assert.Contains("More files", env.Shell.Notifications[^1].Message);
    }

    [Fact]
    public async Task ApplicationNotifications_AnnounceStartAndStopWhenEnabled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var env = await TestEnvironment.CreateAsync(cancellationToken);
        await env.SaveSettingsAsync(s => s.NotifyApplicationEvents = true);
        await using var vm = env.CreateViewModel();
        await vm.LoadAsync();

        await vm.StartCommand.ExecuteAsync(null);
        await env.StopAsync(vm);

        Assert.Equal(["Monitoring started", "Monitoring stopped"], env.Shell.Notifications.Select(n => n.Title));
    }

    // ---- Settings ----

    [Fact]
    public async Task ApiKeys_AreNeverWrittenToDiskInPlainText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var env = await TestEnvironment.CreateAsync(cancellationToken);
        await using (var vm = env.CreateViewModel())
        {
            await vm.LoadAsync();
            vm.DocumentIntelligenceEndpoint = "https://example.cognitiveservices.azure.com/";
            vm.DocumentIntelligenceKey = "super-secret-key";
            await vm.ApplySettingsCommand.ExecuteAsync(null);
            Assert.Contains(vm.Converters, c => c.Name == "Azure Document Intelligence");
        }

        var json = await File.ReadAllTextAsync(env.Paths.SettingsFile, cancellationToken);
        Assert.DoesNotContain("super-secret-key", json);
        Assert.Contains("documentIntelligence", json);

        await using var reloaded = env.CreateViewModel();
        await reloaded.LoadAsync();
        Assert.Equal("super-secret-key", reloaded.DocumentIntelligenceKey);
        Assert.Contains(reloaded.Converters, c => c.Name == "Azure Document Intelligence");
    }

    [Fact]
    public async Task DisabledConverters_PersistAndShrinkTheSupportedFormats()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var env = await TestEnvironment.CreateAsync(cancellationToken);
        await using (var vm = env.CreateViewModel())
        {
            await vm.LoadAsync();
            Assert.Contains(".pdf", vm.SupportedFormatsSummary);
            vm.Converters.Single(c => c.Name == "PDF").IsEnabled = false;
            Assert.DoesNotContain(".pdf", vm.SupportedFormatsSummary);
            await vm.ApplySettingsCommand.ExecuteAsync(null);
        }

        await using var reloaded = env.CreateViewModel();
        await reloaded.LoadAsync();

        Assert.False(reloaded.Converters.Single(c => c.Name == "PDF").IsEnabled);
        Assert.DoesNotContain(".pdf", reloaded.SupportedFormatsSummary);
        Assert.Contains("[off] PDF", reloaded.DiagnosticsText);
    }

    [Fact]
    public async Task Diagnostics_ListVersionsButNoDocumentInformation()
    {
        var env = await TestEnvironment.CreateAsync(TestContext.Current.CancellationToken);
        await using var vm = env.CreateViewModel();
        await vm.LoadAsync();

        Assert.Contains("MarkItDownSharp", vm.DiagnosticsText);
        Assert.Contains("Runtime:", vm.DiagnosticsText);
        Assert.Contains("[on]  Text", vm.DiagnosticsText);
        Assert.DoesNotContain(env.Input, vm.DiagnosticsText);
        Assert.False(string.IsNullOrWhiteSpace(vm.ApplicationVersion));
    }

    [Fact]
    public async Task StartWithWindows_RevertsWhenTheShellRefuses()
    {
        var env = await TestEnvironment.CreateAsync(TestContext.Current.CancellationToken);
        env.Shell.StartupResult = new StartupResult(false, "Disabled in Task Manager.");
        await using var vm = env.CreateViewModel();
        await vm.LoadAsync();

        vm.StartWithWindows = true;
        await env.WaitUntilAsync(() => vm.StartupMessage.Length > 0, TestContext.Current.CancellationToken);

        Assert.False(vm.StartWithWindows);
        Assert.Equal("Disabled in Task Manager.", vm.StartupMessage);
    }

    [Fact]
    public async Task StartMonitoringOnLaunch_StartsWhenFoldersAreValid()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var env = await TestEnvironment.CreateAsync(cancellationToken);
        await env.SaveSettingsAsync(s => s.StartMonitoringOnLaunch = true);
        await using var vm = env.CreateViewModel();
        await vm.LoadAsync();

        await vm.StartIfConfiguredAsync();

        Assert.True(vm.IsRunning);
        await env.StopAsync(vm);
    }

    [Fact]
    public async Task MonitoringTuning_ComesFromSettings()
    {
        var env = await TestEnvironment.CreateAsync(TestContext.Current.CancellationToken);
        await env.SaveSettingsAsync(s =>
        {
            s.DebounceMilliseconds = 120;
            s.ReconciliationSeconds = 7;
            s.QueueCapacity = 25;
            s.QueueFullBehavior = QueueFullBehavior.Drop;
            s.MaxRetries = 5;
        });
        await using var vm = env.CreateViewModel();

        await vm.LoadAsync();

        Assert.Equal(120, vm.DebounceMilliseconds);
        Assert.Equal(7, vm.ReconciliationSeconds);
        Assert.Equal(25, vm.QueueCapacity);
        Assert.Equal((int)QueueFullBehavior.Drop, vm.QueueFullIndex);
        Assert.Equal(5, vm.MaxRetries);
    }

    // ---- History and crash reports ----

    [Fact]
    public async Task HistoryStore_RecoversFromACorruptFile()
    {
        var env = await TestEnvironment.CreateAsync(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(env.Paths.DataDirectory);
        await File.WriteAllTextAsync(env.Paths.HistoryFile, "{ not valid json", TestContext.Current.CancellationToken);

        var records = await new JobHistoryStore(env.Paths).LoadAsync();

        Assert.Empty(records);
        Assert.True(File.Exists(env.Paths.HistoryFile + ".corrupt"));
    }

    [Fact]
    public async Task HistoryStore_KeepsOnlyTheNewestRecords()
    {
        var env = await TestEnvironment.CreateAsync(TestContext.Current.CancellationToken);
        var store = new JobHistoryStore(env.Paths);
        var records = Enumerable.Range(0, JobHistoryStore.MaxRecords + 25)
            .Select(i => new JobRecord($"C:\\in\\{i}.txt", $"C:\\out\\{i}.md", ConversionJobState.Completed, null, DateTimeOffset.UnixEpoch.AddMinutes(i), DateTimeOffset.UnixEpoch.AddMinutes(i)));

        await store.SaveAsync(records);
        var loaded = await store.LoadAsync();

        Assert.Equal(JobHistoryStore.MaxRecords, loaded.Count);
        Assert.Equal($"C:\\in\\{JobHistoryStore.MaxRecords + 24}.txt", loaded[0].SourcePath);
    }

    [Fact]
    public async Task CrashReports_ContainNoMessagesPathsOrDocumentContent()
    {
        var env = await TestEnvironment.CreateAsync(TestContext.Current.CancellationToken);
        var reporter = new CrashReporter(env.Paths);
        Exception exception;
        try
        {
            ThrowWithSecrets();
            throw new InvalidOperationException();
        }
        catch (IOException ex)
        {
            exception = ex;
        }

        var path = reporter.Write(exception, "test");

        Assert.NotNull(path);
        var report = await File.ReadAllTextAsync(path!, TestContext.Current.CancellationToken);
        Assert.Contains("System.IO.IOException", report);
        Assert.Contains("System.InvalidOperationException", report);
        Assert.Contains("ThrowWithSecrets", report);
        Assert.DoesNotContain("secret-contract.docx", report);
        Assert.DoesNotContain("Confidential merger", report);
        Assert.DoesNotContain("PresentationTests.cs", report);
        Assert.DoesNotContain(@"C:\", report);
        Assert.Equal(1, reporter.ReportCount);
    }

    [Fact]
    public async Task CrashReports_AreLimitedInNumber()
    {
        var env = await TestEnvironment.CreateAsync(TestContext.Current.CancellationToken);
        var reporter = new CrashReporter(env.Paths);

        for (var i = 0; i < 25; i++)
        {
            reporter.Write(new InvalidOperationException("x"), "test");
            await Task.Delay(2, TestContext.Current.CancellationToken);
        }

        Assert.Equal(20, reporter.ReportCount);
    }

    private static void ThrowWithSecrets()
    {
        try
        {
            throw new InvalidOperationException(@"Confidential merger terms in C:\Users\me\secret-contract.docx");
        }
        catch (Exception inner)
        {
            throw new IOException(@"Could not read C:\Users\me\secret-contract.docx", inner);
        }
    }

    private sealed class TestEnvironment
    {
        private TestEnvironment(string root)
        {
            Root = root;
            Input = Path.Combine(root, "input");
            Output = Path.Combine(root, "output");
            Paths = new AppPaths(Path.Combine(root, "appdata"));
            Directory.CreateDirectory(Input);
        }

        public string Root { get; }
        public string Input { get; }
        public string Output { get; }
        public AppPaths Paths { get; }
        public FakeShell Shell { get; } = new();
        public ManualDispatcher Dispatcher { get; } = new();

        public static async Task<TestEnvironment> CreateAsync(CancellationToken cancellationToken)
        {
            var env = new TestEnvironment(Path.Combine(Path.GetTempPath(), "MarkItDownSharpTests", Guid.NewGuid().ToString("N")));
            await env.SaveSettingsAsync(_ => { });
            return env;
        }

        public MainViewModel CreateViewModel() =>
            new(Dispatcher, Shell, Paths, new ReversingProtector());

        /// <summary>Waits for a condition while running view-model work on this thread, as a UI thread would.</summary>
        public async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (true)
            {
                Dispatcher.Pump();
                if (condition())
                {
                    return;
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("The condition was not met in time.");
                }

                await Task.Delay(25, cancellationToken);
            }
        }

        public async Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < until)
            {
                Dispatcher.Pump();
                await Task.Delay(25, cancellationToken);
            }
        }

        public async Task StopAsync(MainViewModel vm)
        {
            await vm.StopCommand.ExecuteAsync(null);
            Dispatcher.Pump();
        }

        public async Task SaveSettingsAsync(Action<AppSettings> change)
        {
            var store = new SettingsStore(Paths, new ReversingProtector());
            var settings = File.Exists(Paths.SettingsFile) ? await store.LoadAsync() : new AppSettings();
            settings.SourceDirectory = Input;
            settings.OutputDirectory = Output;
            settings.HasSeenWelcome = settings.HasSeenWelcome;
            settings.DebounceMilliseconds = 50;
            settings.ReconciliationSeconds = 1;
            change(settings);
            await store.SaveAsync(settings);
        }
    }

    /// <summary>Collects posted work and runs it only when a test pumps, which keeps view-model access on one thread.</summary>
    private sealed class ManualDispatcher : IUiDispatcher
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _queue = new();

        public void Post(Action action) => _queue.Enqueue(action);

        public void Pump()
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    /// <summary>A reversible stand-in for DPAPI so the tests also run on non-Windows machines.</summary>
    private sealed class ReversingProtector : ISecretProtector
    {
        public string? Protect(string secret) => Convert.ToBase64String(Encoding.UTF8.GetBytes(new string(secret.Reverse().ToArray())));

        public string? Unprotect(string protectedSecret) => new string(Encoding.UTF8.GetString(Convert.FromBase64String(protectedSecret)).Reverse().ToArray());
    }

    private sealed class FakeShell : IShellServices
    {
        public List<string> Opened { get; } = [];
        public List<string> Revealed { get; } = [];
        public List<(string Title, string Message)> Notifications { get; } = [];
        public StartupResult StartupResult { get; set; } = new(true, "ok");

        public void OpenFile(string path) => Opened.Add(path);

        public void ShowInFolder(string path) => Revealed.Add(path);

        public void Notify(string title, string message)
        {
            lock (Notifications)
            {
                Notifications.Add((title, message));
            }
        }

        public Task<StartupResult> SetStartWithWindowsAsync(bool enabled) => Task.FromResult(StartupResult);
    }
}
