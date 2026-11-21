using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MarkItDownSharp.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel(DispatcherQueue);
        _ = ViewModel.LoadAsync();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 720));
        Closed += MainWindow_Closed;
    }

    public MainWindowViewModel ViewModel { get; }

    private async void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null)
        {
            ViewModel.SourceDirectory = path;
        }
    }

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null)
        {
            ViewModel.OutputDirectory = path;
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args) =>
        await ViewModel.DisposeAsync();
}
