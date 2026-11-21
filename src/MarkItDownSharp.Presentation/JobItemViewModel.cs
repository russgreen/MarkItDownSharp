using CommunityToolkit.Mvvm.ComponentModel;
using MarkItDownSharp.Worker;

namespace MarkItDownSharp.Presentation;

/// <summary>The state of a job at the moment an event was raised. Applied later on the UI thread.</summary>
public sealed record JobSnapshot(
    string OutputPath,
    ConversionJobState State,
    string? Error,
    int Attempts,
    DateTimeOffset QueuedAt,
    DateTimeOffset? CompletedAt)
{
    public static JobSnapshot From(ConversionJob job) =>
        new(job.OutputPath, job.State, job.Error, job.Attempts, job.QueuedAt, job.CompletedAt);
}

public sealed partial class JobItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(DisplayMessage), nameof(CanRetry), nameof(CanCancel), nameof(IsFinished))]
    public partial ConversionJobState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayMessage), nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayMessage))]
    public partial string OutputPath { get; set; }

    [ObservableProperty]
    public partial int Attempts { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? CompletedAt { get; set; }

    public JobItemViewModel(ConversionJob job)
    {
        Job = job;
        SourcePath = job.SourcePath;
        OutputPath = job.OutputPath;
        QueuedAt = job.QueuedAt;
        Apply(JobSnapshot.From(job));
    }

    public ConversionJob Job { get; private set; }
    public string SourcePath { get; }
    public DateTimeOffset QueuedAt { get; private set; }
    public string FileName => Path.GetFileName(SourcePath);
    public bool IsFinished => State is ConversionJobState.Completed or ConversionJobState.Failed or ConversionJobState.Skipped or ConversionJobState.Cancelled;
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool CanRetry => IsFinished && State != ConversionJobState.Completed;
    public bool CanCancel => !IsFinished;
    public string DisplayMessage => HasError ? Error! : OutputPath;

    public string StateText => State switch
    {
        ConversionJobState.Queued => "Queued",
        ConversionJobState.WaitingForFile => "Waiting for file",
        ConversionJobState.Processing => "Converting",
        ConversionJobState.Completed => "Completed",
        ConversionJobState.Failed => "Failed",
        ConversionJobState.Skipped => "Skipped",
        ConversionJobState.Cancelled => "Cancelled",
        ConversionJobState.Retrying => "Retrying",
        _ => State.ToString()
    };

    /// <summary>Points this row at a newer job object for the same source file (after a restart or a rescan).</summary>
    public void Rebind(ConversionJob job, JobSnapshot snapshot)
    {
        Job = job;
        Apply(snapshot);
    }

    public void Update(JobSnapshot snapshot) => Apply(snapshot);

    /// <summary>The persisted form of what this row currently shows.</summary>
    public JobRecord ToRecord() => new(SourcePath, OutputPath, State, Error, QueuedAt, CompletedAt);

    private void Apply(JobSnapshot snapshot)
    {
        if (QueuedAt != snapshot.QueuedAt)
        {
            QueuedAt = snapshot.QueuedAt;
            OnPropertyChanged(nameof(QueuedAt));
        }

        OutputPath = snapshot.OutputPath;
        Error = snapshot.Error;
        Attempts = snapshot.Attempts;
        CompletedAt = snapshot.CompletedAt;
        State = snapshot.State;
    }
}
