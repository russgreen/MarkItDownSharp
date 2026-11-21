namespace MarkItDownSharp.Worker;

public enum ConversionJobState
{
    Queued,
    WaitingForFile,
    Processing,
    Completed,
    Failed,
    Skipped
}
