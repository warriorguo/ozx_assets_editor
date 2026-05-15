using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OAE.App.Controls;
using OAE.App.Forms;
using OAE.App.ViewModels;

namespace OAE.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vmHooked;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RewireFormHook();
    }

    private void RewireFormHook()
    {
        if (_vmHooked is not null)
            _vmHooked.EntityFormChanged -= OnEntityChanged;
        _vmHooked = DataContext as MainWindowViewModel;
        if (_vmHooked is not null)
            _vmHooked.EntityFormChanged += OnEntityChanged;
        OnEntityChanged();
    }

    private void OnEntityChanged()
    {
        RebuildForm();
        RebuildImages();
    }

    private void RebuildForm()
    {
        var host = this.FindControl<StackPanel>("FormHost");
        if (host is null) return;
        host.Children.Clear();
        if (_vmHooked is null) return;
        if (_vmHooked.CurrentSchema is null || _vmHooked.CurrentEntity is null)
        {
            host.Children.Add(new TextBlock
            {
                Text = "(select an entity to edit)",
                Opacity = 0.5,
                FontStyle = Avalonia.Media.FontStyle.Italic,
            });
            return;
        }
        var ctx = new FormContext(
            Importer: _vmHooked.Importer,
            ProjectRoot: string.IsNullOrEmpty(_vmHooked.Config.ProjectRoot) ? null : _vmHooked.Config.ProjectRoot,
            EntityId: _vmHooked.SelectedEntity?.Id,
            OnImportCompleted: () => _vmHooked!.ReloadCurrentEntity());
        var control = EntityFormBuilder.Build(_vmHooked.CurrentSchema, _vmHooked.CurrentEntity, _vmHooked.NotifyFormMutated, ctx);
        host.Children.Add(control);
    }

    private void RebuildImages()
    {
        var host = this.FindControl<WrapPanel>("ImagesHost");
        if (host is null) return;
        host.Children.Clear();
        if (_vmHooked is null) return;
        if (_vmHooked.SelectedEntity is null || _vmHooked.CurrentEntity is null)
        {
            host.Children.Add(new TextBlock
            {
                Text = "(select an entity to see its image assets)",
                Opacity = 0.5,
                FontStyle = Avalonia.Media.FontStyle.Italic,
            });
            return;
        }
        if (_vmHooked.ResolvedAssets.Count == 0)
        {
            host.Children.Add(new TextBlock
            {
                Text = "(no image-bearing fields on this entity, or no asset locator yet)",
                Opacity = 0.5,
                FontStyle = Avalonia.Media.FontStyle.Italic,
            });
            return;
        }
        var projectRoot = _vmHooked.Config.ProjectRoot;
        var entityId = _vmHooked.SelectedEntity.Id;
        foreach (var asset in _vmHooked.ResolvedAssets)
        {
            var card = new AssetCard();
            card.Configure(asset, _vmHooked.Importer, _vmHooked.Resizer, projectRoot, entityId);
            card.ImportCompleted += () => _vmHooked!.ReloadCurrentEntity();
            host.Children.Add(card);
        }
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

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.Save();
    }

    private void OnRevertClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.Revert();
    }
}
