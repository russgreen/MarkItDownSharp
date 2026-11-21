using CommunityToolkit.Mvvm.ComponentModel;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Presentation;

public sealed partial class ConverterItemViewModel : ObservableObject
{
    private readonly Action<ConverterItemViewModel, bool> _setEnabled;
    private bool _updating;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public ConverterItemViewModel(ConverterInfo info, Action<ConverterItemViewModel, bool> setEnabled)
    {
        _setEnabled = setEnabled;
        Name = info.Name;
        Description = info.Description;
        Extensions = string.Join(" ", info.Extensions);
        Priority = info.Priority;
        Origin = info.Origin;
        Version = info.Version;
        RequiresNetwork = info.RequiresNetwork;
        _updating = true;
        IsEnabled = info.IsEnabled;
        _updating = false;
    }

    public string Name { get; }
    public string Description { get; }
    public string Extensions { get; }
    public int Priority { get; }
    public string Origin { get; }
    public string Version { get; }
    public bool RequiresNetwork { get; }

    public string Details =>
        $"{Origin} · {Version} · priority {Priority}" + (RequiresNetwork ? " · sends document content to a network service" : string.Empty);

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_updating)
        {
            _setEnabled(this, value);
        }
    }
}
