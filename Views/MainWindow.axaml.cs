using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GUITCPClient.ViewModels;

namespace GUITCPClient.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public void Construct() {
        var toplevel = TopLevel.GetTopLevel(this);

        if(toplevel is null) return;

        ((MainWindowViewModel)DataContext!)!.SelectFileFromFilesystem = toplevel.StorageProvider.OpenFilePickerAsync;

        ((MainWindowViewModel)DataContext).SelectFileFromPath = toplevel!.StorageProvider.TryGetFileFromPathAsync;
    }
}