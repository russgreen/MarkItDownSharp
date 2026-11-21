namespace MarkItDownSharp.Worker;

// Values are persisted in the job history file. Append new states; never renumber existing ones.
public enum ConversionJobState
{
    Queued,
    WaitingForFile,
    Processing,
    Completed,
    Failed,
    Skipped,
    Cancelled,
    Retrying
}
