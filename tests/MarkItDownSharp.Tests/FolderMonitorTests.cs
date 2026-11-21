using MarkItDownSharp.Converters;
using MarkItDownSharp.Core;
using MarkItDownSharp.Worker;

namespace MarkItDownSharp.Tests;

public sealed class FolderMonitorTests
{
    [Fact]
    public async Task Monitor_ConvertsNewFileAndPreservesSubdirectories()
    {
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
        await monitor.StartAsync(new MonitorOptions(input.FullName, output));
        var nested = Directory.CreateDirectory(Path.Combine(input.FullName, "nested"));

        await File.WriteAllTextAsync(Path.Combine(nested.FullName, "notes.txt"), "converted");
        var job = await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(ConversionJobState.Completed, job.State);
        Assert.Equal("converted", await File.ReadAllTextAsync(Path.Combine(output, "nested", "notes.md")));
    }

    [Fact]
    public async Task Monitor_VersionsExistingOutputWhenConfigured()
    {
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Directory.CreateDirectory(Path.Combine(root, "output"));
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "notes.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(output.FullName, "notes.md"), "old");
        await using var monitor = CreateMonitor();
        var completed = new TaskCompletionSource<ConversionJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.JobChanged += (_, job) =>
        {
            if (job.State == ConversionJobState.Completed)
            {
                completed.TrySetResult(job);
            }
        };

        await monitor.StartAsync(new MonitorOptions(
            input.FullName,
            output.FullName,
            ProcessExistingFiles: true,
            CollisionBehavior: CollisionBehavior.Version));
        var job = await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.EndsWith("notes (1).md", job.OutputPath);
        Assert.Equal("new", await File.ReadAllTextAsync(job.OutputPath));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(output.FullName, "notes.md")));
    }

    [Fact]
    public async Task Monitor_RejectsOutputInsideInput()
    {
        var root = CreateTemporaryDirectory();
        var input = Directory.CreateDirectory(Path.Combine(root, "input"));
        var output = Path.Combine(input.FullName, "output");
        await using var monitor = CreateMonitor();

        await Assert.ThrowsAsync<ArgumentException>(
            () => monitor.StartAsync(new MonitorOptions(input.FullName, output)));
    }

    private static FolderMonitorService CreateMonitor() =>
        new(new MarkItDownService([new TextConverter(), new CsvConverter(), new HtmlConverter(), new StructuredTextConverter()]));

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MarkItDownSharpTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
