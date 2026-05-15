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
    /// Apply a folder selected by the View's folder picker: validate, persist,
    /// and (eventually) hot-swap the store. Until OAE-3 lands the real fsstore,
    /// the store stays a <see cref="StubStore"/> with an updated reason.
    /// </summary>
    public void ApplyProjectRoot(string path)
    {
        Config.ProjectRoot = path;
        Config.Save(ConfigPath);
        var resolved = ResolvedConfig.Resolve(ConfigPath, Config);
        _store.Swap(new StubStore(resolved.UsesFallback
            ? resolved.FallbackReason ?? "fallback"
            : "fsstore not implemented yet (OAE-3)"));
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
            StatusDetail = $"GameData/ at {resolved.GameDataDir}";
        }
    }
}
