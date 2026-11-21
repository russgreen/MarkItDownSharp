using CommunityToolkit.Mvvm.ComponentModel;
using MarkItDownSharp.Worker;

namespace MarkItDownSharp.Desktop;

public sealed partial class JobItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial ConversionJobState State { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public JobItemViewModel(ConversionJob job)
    {
        SourcePath = job.SourcePath;
        OutputPath = job.OutputPath;
        State = job.State;
        Error = job.Error;
    }

    public string SourcePath { get; }
    public string OutputPath { get; private set; }
    public string FileName => Path.GetFileName(SourcePath);
    public string DisplayMessage => string.IsNullOrWhiteSpace(Error) ? OutputPath : Error;

    public void Update(ConversionJob job)
    {
        OutputPath = job.OutputPath;
        State = job.State;
        Error = job.Error;
        OnPropertyChanged(nameof(OutputPath));
        OnPropertyChanged(nameof(DisplayMessage));
    }
}
