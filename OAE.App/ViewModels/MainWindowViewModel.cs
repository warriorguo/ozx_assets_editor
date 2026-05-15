using CommunityToolkit.Mvvm.ComponentModel;
using OAE.Core.Config;
using OAE.Core.Store;

namespace OAE.App.ViewModels;

/// <summary>
/// Drives the main window: shows which project is mounted, what its health is,
/// and surfaces the 'Open Project…' action. The folder-pick itself happens in
/// the View (needs <c>StorageProvider</c>); the View calls
/// <see cref="ApplyProjectRoot"/> with the chosen path.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly HotSwapStore _store;

    public MainWindowViewModel() : this(new HotSwapStore(new StubStore()), new OaeConfig(), OaeConfig.DefaultPath)
    {
        // designer-friendly default ctor; production wiring comes through App.OnFrameworkInitializationCompleted.
    }

    public MainWindowViewModel(HotSwapStore store, OaeConfig config, string configPath)
    {
        _store = store;
        Config = config;
        ConfigPath = configPath;
        // Mount the configured project on construction so re-launches reflect saved state.
        var resolved = ResolvedConfig.Resolve(ConfigPath, Config);
        _store.Swap(StoreFactory.CreateForProject(resolved.UsesFallback ? null : resolved.GameDataDir));
        Refresh();
    }

    public OaeConfig Config { get; private set; }
    public string ConfigPath { get; }

    [ObservableProperty]
    private string _projectRootDisplay = "(no project)";

    [ObservableProperty]
    private string _statusText = "Not set";

    [ObservableProperty]
    private string _statusDetail = "Pick a project to begin.";

    [ObservableProperty]
    private bool _isProjectValid;

    /// <summary>
    /// Apply a folder selected by the View's folder picker: persist the choice,
    /// hot-swap the store to a real <see cref="FsStore"/> when GameData/ is
    /// present, otherwise fall back to <see cref="StubStore"/>.
    /// </summary>
    public void ApplyProjectRoot(string path)
    {
        Config.ProjectRoot = path;
        Config.Save(ConfigPath);
        var resolved = ResolvedConfig.Resolve(ConfigPath, Config);
        _store.Swap(StoreFactory.CreateForProject(resolved.UsesFallback ? null : resolved.GameDataDir));
        Refresh();
    }

    private void Refresh()
    {
        var resolved = ResolvedConfig.Resolve(ConfigPath, Config);
        ProjectRootDisplay = string.IsNullOrEmpty(Config.ProjectRoot) ? "(no project)" : Config.ProjectRoot;
        if (resolved.UsesFallback)
        {
            IsProjectValid = false;
            StatusText = string.IsNullOrEmpty(Config.ProjectRoot) ? "Not set" : "Invalid";
            StatusDetail = resolved.FallbackReason ?? "unknown reason";
        }
        else
        {
            IsProjectValid = true;
            StatusText = "Valid";
            var detail = $"GameData/ at {resolved.GameDataDir}";
            if (_store.Inner is FsStore fs)
                detail = $"{fs.TotalEntityCount()} entities across {FsStore.Types.Count} types";
            StatusDetail = detail;
        }
    }
}
