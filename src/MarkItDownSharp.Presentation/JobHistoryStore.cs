using System.Text.Json;
using System.Text.Json.Serialization;
using MarkItDownSharp.Worker;

namespace MarkItDownSharp.Presentation;

public sealed record JobRecord(
    string SourcePath,
    string OutputPath,
    ConversionJobState State,
    string? Error,
    DateTimeOffset QueuedAt,
    DateTimeOffset? CompletedAt)
{
    public static JobRecord From(ConversionJob job) =>
        new(job.SourcePath, job.OutputPath, job.State, job.Error, job.QueuedAt, job.CompletedAt);

    public ConversionJob ToJob() =>
        ConversionJob.Restore(SourcePath, OutputPath, State, Error, QueuedAt, CompletedAt);
}

/// <summary>
/// Remembers finished jobs (completed, failed, skipped, cancelled) so the list survives application restarts.
/// </summary>
public sealed class JobHistoryStore
{
    public const int MaxRecords = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JobHistoryStore(AppPaths? paths = null)
    {
        _path = (paths ?? AppPaths.Default).HistoryFile;
    }

    public async Task<IReadOnlyList<JobRecord>> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            try
            {
                await using var stream = File.OpenRead(_path);
                var records = await JsonSerializer.DeserializeAsync<List<JobRecord>>(stream, JsonOptions) ?? [];
                return records;
            }
            catch (JsonException)
            {
                // Keep the unreadable file for inspection and start with an empty history.
                File.Move(_path, _path + ".corrupt", overwrite: true);
                return [];
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<JobRecord> records)
    {
        var trimmed = records.OrderByDescending(r => r.CompletedAt ?? r.QueuedAt).Take(MaxRecords).ToList();
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, trimmed, JsonOptions);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
