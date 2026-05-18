using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using OAE.Core.Config;
using OAE.Core.Importer;
using OAE.Core.References;
using OAE.Core.Resources;
using OAE.Core.Schema;
using OAE.Core.Store;
using OAE.Core.Templates;

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
        Importer = new AssetImporter(config.ImportAssetSkillPath);
        Resizer = new SipsImageResizer();
        References = new ReferenceIndex();
        var resolved = ResolvedConfig.Resolve(ConfigPath, Config);
        _store.Swap(StoreFactory.CreateForProject(resolved.UsesFallback ? null : resolved.GameDataDir));
        RebuildAssetLocator();
        References.Rebuild(_store);
        RefreshStatus();
        RefreshTypes();
    }

    public OaeConfig Config { get; }
    public string ConfigPath { get; }
    public HotSwapStore Store => _store;
    public AssetImporter Importer { get; }
    public IImageResizer Resizer { get; }
    public AssetLocator? AssetLocator { get; private set; }
    public IReadOnlyList<ResolvedAsset> ResolvedAssets { get; private set; } = Array.Empty<ResolvedAsset>();
    public ReferenceIndex References { get; }

    /// <summary>
    /// Select <paramref name="typeId"/> in the left pane and <paramref name="entityId"/>
    /// in the middle pane, loading that entity into the form. Used by the
    /// reference picker's '→' jump button.
    /// </summary>
    public void JumpToEntity(string typeId, string entityId)
    {
        var typeItem = Types.FirstOrDefault(t => t.Id == typeId);
        if (typeItem is null) return;
        SelectedType = typeItem;
        // OnSelectedTypeChanged refreshes Entities synchronously; pick the target id.
        var entityItem = Entities.FirstOrDefault(e => e.Id == entityId);
        if (entityItem is not null) SelectedEntity = entityItem;
    }

    /// <summary>
    /// Re-Get the current entity from the store and rebuild the form. Used
    /// after an asset import that may have mutated the entity JSON on disk.
    /// </summary>
    public void ReloadCurrentEntity() => LoadSelectedEntity();

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
        RebuildAssetLocator();
        References.Rebuild(_store);
        RefreshStatus();
        RefreshTypes();
    }

    private void RebuildAssetLocator()
    {
        if (string.IsNullOrEmpty(Config.ProjectRoot) || !Directory.Exists(Config.ProjectRoot))
        {
            AssetLocator = null;
            return;
        }
        AssetLocator = new AssetLocator(Config.ProjectRoot);
        try { AssetLocator.Build(); } catch { /* index is best-effort */ }
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
            // A new id may have been added (or a rename, eventually) — keep the
            // picker dropdowns in sync.
            References.Rebuild(_store);
        }
        catch (Exception ex)
        {
            SaveStatus = $"Save failed: {ex.Message}";
        }
    }

    public void Revert() => LoadSelectedEntity();

    /// <summary>
    /// Create a new entity of <paramref name="typeId"/> from
    /// <paramref name="templateId"/>, writing the template body with
    /// <c>id</c> (and <c>displayName</c> when supplied) overridden. Returns
    /// the error message on failure, <c>null</c> on success.
    /// </summary>
    public string? CreateFromTemplate(string typeId, string templateId, string newId, string? displayName)
    {
        var template = TemplateLoader.Get(typeId, templateId);
        if (template is null) return $"template not found: {typeId}/{templateId}";

        string json;
        try
        {
            json = TemplateLoader.BuildBodyForNewEntity(template, newId, displayName) + "\n";
        }
        catch (Exception ex) { return $"template body parse failed: {ex.Message}"; }

        try
        {
            _store.Create(typeId, newId, json);
        }
        catch (Exception ex) { return ex.Message; }

        // Refresh the navigation + caches and surface the new entity.
        References.Rebuild(_store);
        RefreshEntities();
        var newItem = Entities.FirstOrDefault(e => e.Id == newId);
        if (newItem is not null) SelectedEntity = newItem;
        return null;
    }

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
            ResolvedAssets = (CurrentSchema is not null && CurrentEntity is not null && AssetLocator is not null)
                ? AssetResolver.Resolve(CurrentSchema, CurrentEntity, AssetLocator)
                : Array.Empty<ResolvedAsset>();
            IsDirty = false;
            SaveStatus = string.Empty;
        }
        catch (Exception ex)
        {
            CurrentEntity = null;
            CurrentSchema = null;
            ResolvedAssets = Array.Empty<ResolvedAsset>();
            RawJson = $"// load failed: {ex.Message}";
            SaveStatus = string.Empty;
        }
        EntityFormChanged?.Invoke();
    }

    private void ClearForm()
    {
        CurrentEntity = null;
        CurrentSchema = null;
        ResolvedAssets = Array.Empty<ResolvedAsset>();
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
