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
            OnImportCompleted: () => _vmHooked!.ReloadCurrentEntity(),
            References: _vmHooked.References,
            OnJump: (type, id) => _vmHooked!.JumpToEntity(type, id));
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

    private async void OnImagesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var projectRoot = vm.Config.ProjectRoot;
        if (string.IsNullOrEmpty(projectRoot)) return;
        var window = new ImagesBrowserWindow();
        window.Configure(projectRoot);
        await window.ShowDialog(this);
    }

    private async void OnSoundsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var projectRoot = vm.Config.ProjectRoot;
        if (string.IsNullOrEmpty(projectRoot)) return;

        var path = OAE.Core.Resources.SoundConfigStore.DefaultPathFor(projectRoot);
        if (!System.IO.File.Exists(path))
        {
            vm.SaveStatus = $"SoundConfig.asset not found at {path}";
            return;
        }
        var store = new OAE.Core.Resources.SoundConfigStore();
        store.Load(path);

        var window = new SoundConfigWindow();
        window.Configure(store);
        await window.ShowDialog(this);

        // SFX entries may have been removed — refresh the picker's 'sounds'
        // virtual-type ids so weapon.fireSoundId etc. stay in sync.
        vm.RebuildReferenceIndex();
        vm.ReloadCurrentEntity();
    }

    private async void OnResourcesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.AssetLocator is null) return;
        var projectRoot = vm.Config.ProjectRoot;
        if (string.IsNullOrEmpty(projectRoot)) return;

        var dbPath = OAE.Core.Resources.ResourcesDbStore.DefaultPathFor(projectRoot);
        if (!System.IO.File.Exists(dbPath))
        {
            vm.SaveStatus = $"ResourcesDB.asset not found at {dbPath}";
            return;
        }

        var store = new OAE.Core.Resources.ResourcesDbStore();
        store.Load(dbPath);

        var window = new ResourcesWindow();
        window.Configure(store, vm.AssetLocator.Meta);
        await window.ShowDialog(this);

        // Refresh the locator's read-only DB cache so other parts of the app
        // (asset resolution, picker dropdowns) see any new keys.
        try { vm.AssetLocator.Build(); }
        catch { /* best effort */ }
        vm.ReloadCurrentEntity();
    }

    private async void OnNewEntityClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedType is null) return;
        var dialog = new NewEntityDialog();
        dialog.Configure(vm.SelectedType.Id, vm.References);
        var result = await dialog.ShowDialog<NewEntityResult?>(this);
        if (result is null) return;
        var err = vm.CreateFromTemplate(vm.SelectedType.Id, result.TemplateId, result.NewId, result.DisplayName);
        if (err is not null)
        {
            // Re-open the dialog with the same values so the user can fix and retry.
            // For v1 we just surface via the save-status line.
            vm.SaveStatus = $"Create failed: {err}";
        }
    }
}
