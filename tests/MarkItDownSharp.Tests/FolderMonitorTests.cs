using System.Collections.Concurrent;
using MarkItDownSharp.Converters;
using MarkItDownSharp.Core;
using MarkItDownSharp.Worker;

namespace MarkItDownSharp.Tests;

public sealed class FolderMonitorTests
{
    private static TimeSpan Timeout => TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Monitor_ConvertsNewFileAndPreservesSubdirectories()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(root, "output");
        await using var monitor = CreateMonitor();
        var completed = new TaskCompletionSource<ConversionJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.JobChanged += (_, job) =>
        {
            if (job.State is ConversionJobState.Completed or ConversionJobState.Failed)
            {
                completed.TrySetResult(job);
            }
        };
        await monitor.StartAsync(FastOptions(input.FullName, output), cancellationToken);
        var nested = Directory.CreateDirectory(Path.Combine(input.FullName, "nested"));

        await File.WriteAllTextAsync(Path.Combine(nested.FullName, "notes.txt"), "converted", cancellationToken);
        var job = await completed.Task.WaitAsync(Timeout, cancellationToken);

        Assert.Equal(ConversionJobState.Completed, job.State);
        Assert.Equal("converted", await File.ReadAllTextAsync(Path.Combine(output, "nested", "notes.md"), cancellationToken));
    }

    [Fact]
    public async Task Monitor_VersionsExistingOutputWhenConfigured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Directory.CreateDirectory(Path.Combine(root, "output"));
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "notes.txt"), "new", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(output.FullName, "notes.md"), "old", cancellationToken);
        await using var monitor = CreateMonitor();
        var completed = new TaskCompletionSource<ConversionJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.JobChanged += (_, job) =>
        {
            if (job.State == ConversionJobState.Completed)
            {
                completed.TrySetResult(job);
            }
        };

        await monitor.StartAsync(
            new MonitorOptions(
                input.FullName,
                output.FullName,
                ProcessExistingFiles: true,
                CollisionBehavior: CollisionBehavior.Version),
            cancellationToken);
        var job = await completed.Task.WaitAsync(Timeout, cancellationToken);

        Assert.EndsWith("notes (1).md", job.OutputPath);
        Assert.Equal("new", await File.ReadAllTextAsync(job.OutputPath, cancellationToken));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(output.FullName, "notes.md"), cancellationToken));
    }

    [Fact]
    public async Task Monitor_SkipsExistingOutputWhenConfigured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Directory.CreateDirectory(Path.Combine(root, "output"));
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "notes.txt"), "new", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(output.FullName, "notes.md"), "old", cancellationToken);
        await using var monitor = CreateMonitor();
        var finished = Finished(monitor);

        await monitor.StartAsync(
            FastOptions(input.FullName, output.FullName, existing: true) with { CollisionBehavior = CollisionBehavior.Skip },
            cancellationToken);
        var job = await finished.WaitAsync(Timeout, cancellationToken);

        Assert.Equal(ConversionJobState.Skipped, job.State);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(output.FullName, "notes.md"), cancellationToken));
    }

    [Fact]
    public async Task Monitor_RejectsOutputEqualToInput()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        await using var monitor = CreateMonitor();

        await Assert.ThrowsAsync<ArgumentException>(
            () => monitor.StartAsync(new MonitorOptions(input.FullName, input.FullName), cancellationToken));
    }

    [Fact]
    public async Task Monitor_DoesNotReprocessOutputNestedInsideInput()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(input.FullName, "converted");
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "notes.txt"), "hello", cancellationToken);
        await using var monitor = CreateMonitor();
        var created = new List<string>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.JobCreated += (_, job) => { lock (created) { created.Add(job.SourcePath); } };
        monitor.JobChanged += (_, job) =>
        {
            if (job.State == ConversionJobState.Completed)
            {
                completed.TrySetResult();
            }
        };

        await monitor.StartAsync(
            FastOptions(input.FullName, output, existing: true) with { ReconciliationInterval = TimeSpan.FromMilliseconds(200) },
            cancellationToken);
        await completed.Task.WaitAsync(Timeout, cancellationToken);
        await Task.Delay(1000, cancellationToken);

        lock (created)
        {
            Assert.Single(created);
        }

        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(output, "notes.md"), cancellationToken));
        Assert.False(File.Exists(Path.Combine(output, "notes (1).md")));
    }

    [Fact]
    public async Task Monitor_IgnoresTemporaryFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(root, "output");
        await using var monitor = CreateMonitor();
        var created = new List<string>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.JobCreated += (_, job) => { lock (created) { created.Add(job.FileName); } };
        monitor.JobChanged += (_, job) =>
        {
            if (job.State == ConversionJobState.Completed)
            {
                completed.TrySetResult();
            }
        };
        await monitor.StartAsync(FastOptions(input.FullName, output), cancellationToken);

        await File.WriteAllTextAsync(Path.Combine(input.FullName, "~$report.txt"), "lock", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "download.txt.part"), "partial", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "scratch.tmp"), "scratch", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "real.txt"), "real", cancellationToken);
        await completed.Task.WaitAsync(Timeout, cancellationToken);
        await Task.Delay(500, cancellationToken);

        lock (created)
        {
            Assert.Equal(["real.txt"], created);
        }
    }

    [Fact]
    public async Task Monitor_WaitsUntilWriterReleasesFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(root, "output");
        var path = Path.Combine(input.FullName, "big.txt");
        await using var monitor = CreateMonitor();
        var completed = new TaskCompletionSource<ConversionJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.JobChanged += (_, job) =>
        {
            if (job.State == ConversionJobState.Completed)
            {
                completed.TrySetResult(job);
            }
        };
        await monitor.StartAsync(FastOptions(input.FullName, output), cancellationToken);

        // A writer that holds the file exclusively and keeps appending.
        await using (var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await writer.WriteAsync("part one "u8.ToArray(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            await Task.Delay(700, cancellationToken);
            Assert.False(completed.Task.IsCompleted);
            await writer.WriteAsync("part two"u8.ToArray(), cancellationToken);
        }

        await completed.Task.WaitAsync(Timeout, cancellationToken);

        Assert.Equal("part one part two", await File.ReadAllTextAsync(Path.Combine(output, "big.md"), cancellationToken));
    }

    [Fact]
    public async Task Monitor_RetriesTransientFailures()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(root, "output");
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "locked.tst"), "x", cancellationToken);
        var calls = 0;
        var converter = new ScriptedConverter(_ =>
            ++calls < 3 ? throw new IOException("file is locked") : Task.FromResult("done"));
        await using var monitor = new FolderMonitorService(new MarkItDownService([converter]));
        var finished = Finished(monitor);

        await monitor.StartAsync(
            FastOptions(input.FullName, output, existing: true) with { MaxRetries = 3, RetryDelay = TimeSpan.FromMilliseconds(10) },
            cancellationToken);
        var job = await finished.WaitAsync(Timeout, cancellationToken);

        Assert.Equal(ConversionJobState.Completed, job.State);
        Assert.Equal(3, job.Attempts);
        Assert.Null(job.Error);
    }

    [Fact]
    public async Task Monitor_FailsAfterRetriesAreExhausted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "locked.tst"), "x", cancellationToken);
        var converter = new ScriptedConverter(_ => throw new IOException("always locked"));
        await using var monitor = new FolderMonitorService(new MarkItDownService([converter]));
        var finished = Finished(monitor);

        await monitor.StartAsync(
            FastOptions(input.FullName, Path.Combine(root, "output"), existing: true) with { MaxRetries = 2, RetryDelay = TimeSpan.FromMilliseconds(10) },
            cancellationToken);
        var job = await finished.WaitAsync(Timeout, cancellationToken);

        Assert.Equal(ConversionJobState.Failed, job.State);
        Assert.Equal(3, job.Attempts);
        Assert.Equal("always locked", job.Error);
    }

    [Fact]
    public async Task Monitor_DoesNotRetryPermanentFailures_AndCanRetryOnDemand()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(root, "output");
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "doc.tst"), "x", cancellationToken);
        var broken = true;
        var calls = 0;
        var converter = new ScriptedConverter(_ =>
        {
            calls++;
            return broken ? throw new InvalidDataException("corrupt") : Task.FromResult("fixed");
        });
        await using var monitor = new FolderMonitorService(new MarkItDownService([converter]));
        var first = Finished(monitor);
        await monitor.StartAsync(FastOptions(input.FullName, output, existing: true), cancellationToken);
        var job = await first.WaitAsync(Timeout, cancellationToken);
        Assert.Equal(ConversionJobState.Failed, job.State);
        Assert.Equal(1, calls);

        broken = false;
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.JobChanged += (_, changed) =>
        {
            if (changed.State == ConversionJobState.Completed)
            {
                second.TrySetResult();
            }
        };
        Assert.True(await monitor.RetryAsync(job, cancellationToken));
        await second.Task.WaitAsync(Timeout, cancellationToken);

        Assert.Equal(ConversionJobState.Completed, job.State);
        Assert.Equal("fixed", await File.ReadAllTextAsync(Path.Combine(output, "doc.md"), cancellationToken));
    }

    [Fact]
    public async Task Monitor_CanCancelQueuedAndRunningJobs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "a.tst"), "a", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "b.tst"), "b", cancellationToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var converter = new ScriptedConverter(async _ =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout, CancellationToken.None);
            return "never";
        }, honourCancellation: true);
        await using var monitor = new FolderMonitorService(new MarkItDownService([converter]));
        var states = new ConcurrentDictionary<string, ConversionJobState>();
        monitor.JobChanged += (_, job) => states[job.FileName] = job.State;

        await monitor.StartAsync(
            FastOptions(input.FullName, Path.Combine(root, "output"), existing: true) with { WorkerCount = 1 },
            cancellationToken);
        await started.Task.WaitAsync(Timeout, cancellationToken);

        // One job is running and the other is waiting behind it in the queue.
        monitor.CancelAll();
        await WaitUntilAsync(() => states.Count == 2 && states.Values.All(s => s == ConversionJobState.Cancelled), cancellationToken);

        Assert.All(states.Values, s => Assert.Equal(ConversionJobState.Cancelled, s));
        await WaitUntilAsync(() => monitor.PendingCount == 0, cancellationToken);
    }

    [Fact]
    public async Task Monitor_CancelJobTargetsOneFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var path = Path.Combine(input.FullName, "a.tst");
        await File.WriteAllTextAsync(path, "a", cancellationToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var converter = new ScriptedConverter(async _ =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout, CancellationToken.None);
            return "never";
        }, honourCancellation: true);
        await using var monitor = new FolderMonitorService(new MarkItDownService([converter]));
        var finished = Finished(monitor);
        await monitor.StartAsync(FastOptions(input.FullName, Path.Combine(root, "output"), existing: true), cancellationToken);
        await started.Task.WaitAsync(Timeout, cancellationToken);

        Assert.True(monitor.CancelJob(path));
        var job = await finished.WaitAsync(Timeout, cancellationToken);

        Assert.Equal(ConversionJobState.Cancelled, job.State);
    }

    [Fact]
    public async Task Monitor_PauseHoldsQueuedJobsUntilResumed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(root, "output");
        await using var monitor = CreateMonitor();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.JobChanged += (_, job) =>
        {
            if (job.State == ConversionJobState.Completed)
            {
                completed.TrySetResult();
            }
        };
        await monitor.StartAsync(FastOptions(input.FullName, output), cancellationToken);

        monitor.Pause();
        Assert.True(monitor.IsPaused);
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "later.txt"), "later", cancellationToken);
        await Task.Delay(1200, cancellationToken);
        Assert.False(completed.Task.IsCompleted);
        Assert.Equal(1, monitor.PendingCount);

        monitor.Resume();
        await completed.Task.WaitAsync(Timeout, cancellationToken);

        Assert.False(monitor.IsPaused);
        Assert.Equal("later", await File.ReadAllTextAsync(Path.Combine(output, "later.md"), cancellationToken));
    }

    [Fact]
    public async Task Monitor_DropsFilesWhenQueueIsFullAndPicksThemUpLater()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(root, "output");
        for (var i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(input.FullName, $"f{i}.tst"), "x", cancellationToken);
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var converter = new ScriptedConverter(async _ =>
        {
            await gate.Task;
            return "ok";
        });
        await using var monitor = new FolderMonitorService(new MarkItDownService([converter]));
        var errors = new List<string>();
        monitor.MonitorError += (_, ex) => { lock (errors) { errors.Add(ex.Message); } };

        await monitor.StartAsync(
            FastOptions(input.FullName, output, existing: true) with { QueueCapacity = 1, WorkerCount = 1, QueueFullBehavior = QueueFullBehavior.Drop },
            cancellationToken);

        lock (errors)
        {
            Assert.NotEmpty(errors);
            Assert.Contains("queue is full", errors[0]);
        }

        gate.SetResult();
        for (var i = 0; i < 10 && (!Directory.Exists(output) || Directory.GetFiles(output, "*.md").Length < 5); i++)
        {
            await Task.Delay(200, cancellationToken);
            await monitor.ReconcileNowAsync(cancellationToken);
        }

        Assert.Equal(5, Directory.GetFiles(output, "*.md").Length);
    }

    [Fact]
    public async Task Monitor_FlushesQueuedJobsOnGracefulStop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(root, "output");
        for (var i = 0; i < 4; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(input.FullName, $"f{i}.tst"), "x", cancellationToken);
        }

        var converter = new ScriptedConverter(async _ =>
        {
            await Task.Delay(100, CancellationToken.None);
            return "ok";
        });
        var monitor = new FolderMonitorService(new MarkItDownService([converter]));
        await monitor.StartAsync(FastOptions(input.FullName, output, existing: true) with { WorkerCount = 1 }, cancellationToken);

        await monitor.StopAsync(flushQueue: true, TimeSpan.FromSeconds(20));

        Assert.Equal(4, Directory.GetFiles(output, "*.md").Length);
    }

    [Fact]
    public async Task Monitor_CancelsQueuedJobsOnImmediateStop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        for (var i = 0; i < 4; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(input.FullName, $"f{i}.tst"), "x", cancellationToken);
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var converter = new ScriptedConverter(async _ =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout, CancellationToken.None);
            return "never";
        }, honourCancellation: true);
        var monitor = new FolderMonitorService(new MarkItDownService([converter]));
        var states = new ConcurrentDictionary<string, ConversionJobState>();
        monitor.JobChanged += (_, job) => states[job.FileName] = job.State;
        await monitor.StartAsync(FastOptions(input.FullName, Path.Combine(root, "output"), existing: true) with { WorkerCount = 1 }, cancellationToken);
        await started.Task.WaitAsync(Timeout, cancellationToken);

        await monitor.StopAsync();

        Assert.Equal(4, states.Count);
        Assert.All(states.Values, s => Assert.Equal(ConversionJobState.Cancelled, s));
    }

    [Fact]
    public async Task Monitor_ReportsDeletedSourcesAndRemovesStaleOutput()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(root, "output");
        var source = Path.Combine(input.FullName, "gone.txt");
        await File.WriteAllTextAsync(source, "bye", cancellationToken);
        await using var monitor = CreateMonitor();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleted = new TaskCompletionSource<SourceDeletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.JobChanged += (_, job) =>
        {
            if (job.State == ConversionJobState.Completed)
            {
                completed.TrySetResult();
            }
        };
        monitor.SourceDeleted += (_, e) => deleted.TrySetResult(e);
        await monitor.StartAsync(FastOptions(input.FullName, output, existing: true) with { DeleteOutputWhenSourceDeleted = true }, cancellationToken);
        await completed.Task.WaitAsync(Timeout, cancellationToken);
        Assert.True(File.Exists(Path.Combine(output, "gone.md")));

        File.Delete(source);
        var args = await deleted.Task.WaitAsync(Timeout, cancellationToken);

        Assert.Equal(source, args.SourcePath);
        Assert.True(args.OutputDeleted);
        Assert.False(File.Exists(Path.Combine(output, "gone.md")));
    }

    [Fact]
    public async Task Monitor_ReportsADeletionOnceEvenWhenReconciliationRunsAtTheSameTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(root, "output");
        var source = Path.Combine(input.FullName, "gone.txt");
        await File.WriteAllTextAsync(source, "bye", cancellationToken);
        await using var monitor = CreateMonitor();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleted = new ConcurrentBag<string>();
        monitor.JobChanged += (_, job) =>
        {
            if (job.State == ConversionJobState.Completed)
            {
                completed.TrySetResult();
            }
        };
        monitor.SourceDeleted += (_, e) => deleted.Add(e.SourcePath);
        await monitor.StartAsync(FastOptions(input.FullName, output, existing: true), cancellationToken);
        await completed.Task.WaitAsync(Timeout, cancellationToken);

        // Both the file watcher and a reconciliation pass notice the deletion; only one report may result.
        File.Delete(source);
        await monitor.ReconcileNowAsync(cancellationToken);
        await Task.Delay(1500, cancellationToken);

        Assert.Equal([source], deleted.ToArray());
    }

    [Fact]
    public async Task Monitor_ConvertsRenamedFilesUnderTheirNewName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(root, "output");
        await using var monitor = CreateMonitor();
        var completed = new TaskCompletionSource<ConversionJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.JobChanged += (_, job) =>
        {
            if (job.State == ConversionJobState.Completed && job.FileName == "final.txt")
            {
                completed.TrySetResult(job);
            }
        };
        await monitor.StartAsync(FastOptions(input.FullName, output), cancellationToken);

        // Typical "write to a temporary name, then rename" pattern.
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "draft.tmp"), "renamed content", cancellationToken);
        File.Move(Path.Combine(input.FullName, "draft.tmp"), Path.Combine(input.FullName, "final.txt"));
        await completed.Task.WaitAsync(Timeout, cancellationToken);

        Assert.Equal("renamed content", await File.ReadAllTextAsync(Path.Combine(output, "final.md"), cancellationToken));
        Assert.False(File.Exists(Path.Combine(output, "draft.md")));
    }

    [Fact]
    public async Task Monitor_RestoredJobsAreNotRunUntilRetried()
    {
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var job = ConversionJob.Restore(
            Path.Combine(input.FullName, "a.txt"),
            Path.Combine(root, "out", "a.md"),
            ConversionJobState.Failed,
            "boom",
            DateTimeOffset.Now.AddHours(-1),
            DateTimeOffset.Now);
        await using var monitor = CreateMonitor();

        // The monitor is not running, so there is nothing to queue the job on.
        Assert.False(await monitor.RetryAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal(ConversionJobState.Failed, job.State);
        Assert.True(job.IsFinished);
    }

    private static MonitorOptions FastOptions(string input, string output, bool existing = false) =>
        new(input, output, ProcessExistingFiles: existing)
        {
            Debounce = TimeSpan.FromMilliseconds(50),
            StabilityInterval = TimeSpan.FromMilliseconds(50),
            ReconciliationInterval = TimeSpan.FromSeconds(1)
        };

    private static Task<ConversionJob> Finished(FolderMonitorService monitor)
    {
        var source = new TaskCompletionSource<ConversionJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.JobChanged += (_, job) =>
        {
            if (job.IsFinished)
            {
                source.TrySetResult(job);
            }
        };
        return source.Task;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    private static FolderMonitorService CreateMonitor() =>
        new(new MarkItDownService([new TextConverter(), new CsvConverter(), new HtmlConverter(), new StructuredTextConverter()]));

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MarkItDownSharpTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ScriptedConverter(Func<Stream, Task<string>> convert, bool honourCancellation = false) : IMarkItDownConverter, IExtensionProvider
    {
        public int Priority => 0;
        public IReadOnlyCollection<string> Extensions { get; } = [".tst"];
        public bool CanConvert(StreamMetadata metadata) => metadata.Extension == ".tst";

        public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
        {
            var task = convert(stream);
            if (honourCancellation)
            {
                task = task.WaitAsync(cancellationToken);
            }

            return new ConversionResult(await task);
        }
    }
}
