using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OAE.App.ViewModels;

namespace OAE.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick the ozx_base project root",
            AllowMultiple = false,
        });

        var picked = folders.FirstOrDefault();
        if (picked is null) return;

        vm.ApplyProjectRoot(picked.Path.LocalPath);
    }
}
