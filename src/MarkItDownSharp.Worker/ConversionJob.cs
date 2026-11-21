using System.ComponentModel;

namespace MarkItDownSharp.Worker;

public sealed class ConversionJob : INotifyPropertyChanged
{
    private ConversionJobState _state;
    private string? _error;

    public ConversionJob(string sourcePath, string outputPath)
    {
        SourcePath = sourcePath;
        OutputPath = outputPath;
    }

    public string SourcePath { get; }
    public string OutputPath { get; private set; }
    public string FileName => Path.GetFileName(SourcePath);
    public DateTimeOffset QueuedAt { get; } = DateTimeOffset.Now;

    public ConversionJobState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
        }
    }

    public string? Error
    {
        get => _error;
        set
        {
            if (_error == value)
            {
                return;
            }

            _error = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Error)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void SetOutputPath(string outputPath)
    {
        OutputPath = outputPath;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutputPath)));
    }
}
