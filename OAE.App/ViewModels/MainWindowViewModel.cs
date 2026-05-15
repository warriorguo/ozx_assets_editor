using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using OAE.Core.Config;
using OAE.Core.Schema;
using OAE.Core.Store;

namespace OAE.App.ViewModels;

/// <summary>
/// One row in the left pane: an entity type (e.g. "enemies") and its count.
/// </summary>
public sealed partial class TypeListItem : ObservableObject
{
    public TypeListItem(string id, int count) { Id = id; _count = count; }
    public string Id { get; }
    [ObservableProperty] private int _count;
    public string Display => $"{Id} ({Count})";
}

/// <summary>
/// One row in the middle pane: an entity within the selected type.
/// </summary>
public sealed record EntityListItem(string Id, string Path);

/// <summary>
/// Drives the main window. Owns project mounting (from OAE-2/3), the type
/// and entity navigation, and the JSON node + dirty flag the form binds to.
/// The View subscribes to <see cref="EntityFormChanged"/> to rebuild the
/// reflection-driven controls when the selection changes.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions SaveJsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    private readonly HotSwapStore _store;

    public MainWindowViewModel() : this(new HotSwapStore(new StubStore()), new OaeConfig(), OaeConfig.DefaultPath) { }

    public MainWindowViewModel(HotSwapStore store, OaeConfig config, string configPath)
    {
        _store = store;
        Config = config;
        ConfigPath = configPath;
        var resolved = ResolvedConfig.Resolve(ConfigPath, Config);
        _store.Swap(StoreFactory.CreateForProject(resolved.UsesFallback ? null : resolved.GameDataDir));
        RefreshStatus();
        RefreshTypes();
    }

    public OaeConfig Config { get; }
    public string ConfigPath { get; }
    public HotSwapStore Store => _store;

    // ── status banner state ──────────────────────────────────────────────
    [ObservableProperty] private string _projectRootDisplay = "(no project)";
    [ObservableProperty] private string _statusText = "Not set";
    [ObservableProperty] private string _statusDetail = "Pick a project to begin.";
    [ObservableProperty] private bool _isProjectValid;

    // ── type / entity navigation ─────────────────────────────────────────
    public ObservableCollection<TypeListItem> Types { get; } = new();

    [ObservableProperty] private TypeListItem? _selectedType;
    partial void OnSelectedTypeChanged(TypeListItem? value) => RefreshEntities();

    public ObservableCollection<EntityListItem> Entities { get; } = new();

    [ObservableProperty] private string _entityFilter = string.Empty;
    partial void OnEntityFilterChanged(string value) => RefreshEntities();

    [ObservableProperty] private EntityListItem? _selectedEntity;
    partial void OnSelectedEntityChanged(EntityListItem? value) => LoadSelectedEntity();

    // ── form state ───────────────────────────────────────────────────────
    public SchemaModel? CurrentSchema { get; private set; }
    public JsonObject? CurrentEntity { get; private set; }
    [ObservableProperty] private string _rawJson = string.Empty;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _saveStatus = string.Empty;

    /// <summary>Raised when <see cref="CurrentEntity"/> is replaced (entity selected, reverted, etc).</summary>
    public event Action? EntityFormChanged;

    /// <summary>Marks the form dirty and refreshes the raw-JSON tab text.</summary>
    public void NotifyFormMutated()
    {
        IsDirty = true;
        if (CurrentEntity is not null)
            RawJson = CurrentEntity.ToJsonString(SaveJsonOpts);
    }

    /// <summary>
    /// Apply a folder selected by the View's folder picker. See OAE-3.
    /// </summary>
    public void ApplyProjectRoot(string path)
    {
        Config.ProjectRoot = path;
        Config.Save(ConfigPath);
        var resolved = ResolvedConfig.Resolve(ConfigPath, Config);
        _store.Swap(StoreFactory.CreateForProject(resolved.UsesFallback ? null : resolved.GameDataDir));
        RefreshStatus();
        RefreshTypes();
    }

    public void Save()
    {
        if (SelectedType is null || SelectedEntity is null || CurrentEntity is null) return;
        var json = CurrentEntity.ToJsonString(SaveJsonOpts) + "\n";
        try
        {
            _store.Update(SelectedType.Id, SelectedEntity.Id, json);
            IsDirty = false;
            SaveStatus = $"Saved {SelectedEntity.Id} at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Save failed: {ex.Message}";
        }
    }

    public void Revert() => LoadSelectedEntity();

    private void RefreshStatus()
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
            StatusDetail = _store.Inner is FsStore fs
                ? $"{fs.TotalEntityCount()} entities across {FsStore.Types.Count} types"
                : $"GameData/ at {resolved.GameDataDir}";
        }
    }

    private void RefreshTypes()
    {
        Types.Clear();
        foreach (var typeId in EntityTypes.Map.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var count = SafeListCount(typeId);
            Types.Add(new TypeListItem(typeId, count));
        }
        SelectedType = null;
        Entities.Clear();
        ClearForm();
    }

    private void RefreshEntities()
    {
        Entities.Clear();
        if (SelectedType is null) { ClearForm(); return; }
        var filter = EntityFilter.Trim();
        foreach (var e in _store.List(SelectedType.Id))
        {
            if (filter.Length > 0 && !e.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            Entities.Add(new EntityListItem(e.Id, e.Path ?? string.Empty));
        }
        SelectedEntity = null;
        ClearForm();
    }

    private void LoadSelectedEntity()
    {
        if (SelectedType is null || SelectedEntity is null) { ClearForm(); return; }
        try
        {
            var raw = _store.Get(SelectedType.Id, SelectedEntity.Id);
            CurrentEntity = JsonNode.Parse(raw)?.AsObject();
            CurrentSchema = SchemaBuilder.For(EntityTypes.Map[SelectedType.Id]);
            RawJson = CurrentEntity?.ToJsonString(SaveJsonOpts) ?? string.Empty;
            IsDirty = false;
            SaveStatus = string.Empty;
        }
        catch (Exception ex)
        {
            CurrentEntity = null;
            CurrentSchema = null;
            RawJson = $"// load failed: {ex.Message}";
            SaveStatus = string.Empty;
        }
        EntityFormChanged?.Invoke();
    }

    private void ClearForm()
    {
        CurrentEntity = null;
        CurrentSchema = null;
        RawJson = string.Empty;
        IsDirty = false;
        SaveStatus = string.Empty;
        EntityFormChanged?.Invoke();
    }

    private int SafeListCount(string typeId)
    {
        try { return _store.List(typeId).Count; }
        catch { return 0; }
    }
}
