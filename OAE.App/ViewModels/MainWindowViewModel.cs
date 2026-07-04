using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using OAE.Core.Config;
using OAE.Core.Docs;
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
        RebuildReferenceIndex();
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _isDirty;
    [ObservableProperty] private string _saveStatus = string.Empty;
    // Empty = JSON is valid (or no entity loaded). Set when the Raw JSON tab
    // can't be parsed; surfaced in the view and used to gate Save.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _rawJsonError = string.Empty;

    /// <summary>True when there are unsaved changes AND the Raw JSON tab parses cleanly.</summary>
    public bool CanSave => IsDirty && string.IsNullOrEmpty(RawJsonError);

    // Re-entrancy guard: when the form mutates CurrentEntity and we
    // re-serialise the JSON for the Raw tab, the resulting setter call must
    // not parse-and-replace CurrentEntity (would invalidate every form
    // binding's JsonObject reference). Same when loading or clearing.
    private bool _suppressJsonParse;

    // ── undo/redo (OAE-54) ────────────────────────────────────────────────
    // History is kept over serialised snapshots of the canonical model
    // (CurrentEntity), so a single stack unifies edits made through the form
    // and through the Raw JSON tab. Snapshots use SaveJsonOpts so they compare
    // equal to what Save() would write.
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    // The snapshot representing the model's current committed state (top of the
    // "present"). Prior states live in _undo; undone states live in _redo.
    private string _lastSnapshot = string.Empty;
    // The loaded/saved baseline — IsDirty is derived by comparing against it so
    // undoing back to the on-disk state correctly clears the dirty flag.
    private string _loadedBaseline = string.Empty;
    // Guard: restoring a snapshot during Undo/Redo must not itself capture history.
    private bool _applyingHistory;
    // Coalesce a run of consecutive Raw-JSON keystrokes into one undo step
    // (the tab parses on every keystroke; one step per edit-run, not per key).
    private bool _coalesceRaw;

    /// <summary>Which surface produced a model change — drives raw-edit coalescing.</summary>
    private enum EditKind { Form, Raw }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Raised whenever the undo/redo availability changes.</summary>
    public event Action? HistoryChanged;

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        HistoryChanged?.Invoke();
    }

    /// <summary>
    /// Reset the undo/redo history to a fresh baseline (entity loaded, cleared,
    /// or saved). No prior states are reachable after this.
    /// </summary>
    private void ResetHistory(string baseline)
    {
        _undo.Clear();
        _redo.Clear();
        _lastSnapshot = baseline;
        _loadedBaseline = baseline;
        _coalesceRaw = false;
        NotifyHistoryChanged();
    }

    /// <summary>
    /// Record a transition of the canonical model. Pushes the prior snapshot
    /// onto the undo stack and clears the redo stack. A run of Raw-JSON edits
    /// coalesces into the single undo step opened by the first edit of the run.
    /// </summary>
    private void CaptureHistory(EditKind kind)
    {
        if (_applyingHistory || CurrentEntity is null) return;
        var current = CurrentEntity.ToJsonString(SaveJsonOpts);
        if (string.Equals(current, _lastSnapshot, StringComparison.Ordinal)) return;

        if (kind == EditKind.Raw && _coalesceRaw)
        {
            // Already mid raw-edit run — extend the current step, don't open a new one.
            _lastSnapshot = current;
            return;
        }

        _undo.Push(_lastSnapshot);
        _redo.Clear();
        _lastSnapshot = current;
        _coalesceRaw = kind == EditKind.Raw;
        NotifyHistoryChanged();
    }

    /// <summary>Undo the last edit, moving the current state onto the redo stack.</summary>
    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(_lastSnapshot);
        RestoreSnapshot(_undo.Pop());
    }

    /// <summary>Redo the last undone edit.</summary>
    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(_lastSnapshot);
        RestoreSnapshot(_redo.Pop());
    }

    private void RestoreSnapshot(string snapshot)
    {
        _applyingHistory = true;
        try
        {
            CurrentEntity = JsonNode.Parse(snapshot)?.AsObject();
            _lastSnapshot = snapshot;
            _coalesceRaw = false;
            _suppressJsonParse = true;
            RawJson = snapshot;
            _suppressJsonParse = false;
            RawJsonError = string.Empty;
            ResolvedAssets = (CurrentSchema is not null && CurrentEntity is not null && AssetLocator is not null)
                ? AssetResolver.Resolve(CurrentSchema, CurrentEntity, AssetLocator)
                : Array.Empty<ResolvedAsset>();
            IsDirty = !string.Equals(_lastSnapshot, _loadedBaseline, StringComparison.Ordinal);
        }
        finally { _applyingHistory = false; }
        NotifyHistoryChanged();
        EntityFormChanged?.Invoke();
    }

    /// <summary>Raised when <see cref="CurrentEntity"/> is replaced (entity selected, reverted, etc).</summary>
    public event Action? EntityFormChanged;

    /// <summary>Marks the form dirty and refreshes the raw-JSON tab text.</summary>
    public void NotifyFormMutated()
    {
        IsDirty = true;
        if (CurrentEntity is null) return;
        _suppressJsonParse = true;
        try
        {
            RawJson = CurrentEntity.ToJsonString(SaveJsonOpts);
            RawJsonError = string.Empty;
        }
        finally { _suppressJsonParse = false; }
        CaptureHistory(EditKind.Form);
    }

    /// <summary>
    /// Called when the user edits the Raw JSON tab. Parses on every change;
    /// on success swaps <see cref="CurrentEntity"/> and rebuilds the form, on
    /// failure records the parser message and leaves CurrentEntity alone.
    /// </summary>
    partial void OnRawJsonChanged(string value)
    {
        if (_suppressJsonParse) return;
        if (CurrentSchema is null) return; // nothing to bind to

        JsonObject? parsed;
        try
        {
            var node = JsonNode.Parse(value);
            parsed = node?.AsObject();
        }
        catch (Exception ex)
        {
            RawJsonError = ex.Message;
            return;
        }
        if (parsed is null)
        {
            RawJsonError = "JSON must be an object.";
            return;
        }

        CurrentEntity = parsed;
        ResolvedAssets = AssetLocator is not null
            ? AssetResolver.Resolve(CurrentSchema, parsed, AssetLocator)
            : Array.Empty<ResolvedAsset>();
        RawJsonError = string.Empty;
        IsDirty = true;
        CaptureHistory(EditKind.Raw);
        EntityFormChanged?.Invoke();
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
        RebuildReferenceIndex();
        RefreshStatus();
        RefreshTypes();
    }

    /// <summary>
    /// Rebuild the reference picker's id cache. Pulls entity-type ids from
    /// the store and merges any virtual-type sources (e.g. SoundConfig
    /// soundIds for the 'sounds' picker target).
    /// </summary>
    public void RebuildReferenceIndex()
    {
        var virtuals = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var sounds = TryLoadSounds();
        if (sounds is not null) virtuals["sounds"] = sounds;
        References.Rebuild(_store, new ReferenceIndex.RebuildOptions(virtuals));
    }

    private IReadOnlyList<string>? TryLoadSounds()
    {
        if (string.IsNullOrEmpty(Config.ProjectRoot)) return null;
        var path = SoundConfigStore.DefaultPathFor(Config.ProjectRoot);
        if (!File.Exists(path)) return null;
        try
        {
            var store = new SoundConfigStore();
            store.Load(path);
            return store.List().Select(e => e.SoundId).ToList();
        }
        catch { return null; }
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
            // Save is a history boundary (OAE-54): the saved state becomes the
            // new baseline and prior edits are no longer undoable.
            ResetHistory(CurrentEntity.ToJsonString(SaveJsonOpts));
            // A new id may have been added (or a rename, eventually) — keep the
            // picker dropdowns in sync.
            RebuildReferenceIndex();

            var docSuffix = Config.AutoSyncDocs
                ? TrySyncDocs(SelectedType.Id, SelectedEntity.Id, CurrentEntity)
                : string.Empty;
            SaveStatus = $"Saved {SelectedEntity.Id} at {DateTime.Now:HH:mm:ss}{docSuffix}";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Save failed: {ex.Message}";
        }
    }

    private string TrySyncDocs(string typeId, string entityId, JsonObject body)
    {
        if (string.IsNullOrEmpty(Config.ProjectRoot)) return string.Empty;
        var docsRoot = Path.Combine(Config.ProjectRoot, "Documents");
        if (!Directory.Exists(docsRoot)) return string.Empty;
        try
        {
            var writer = new DocSyncWriter();
            var result = writer.SyncEntity(typeId, entityId, body, docsRoot);
            return result.Status switch
            {
                DocSyncStatus.Updated   => $"  ·  doc: {result.Changes.Count} cell(s) updated",
                DocSyncStatus.Unchanged => "  ·  doc: no changes",
                DocSyncStatus.NotFound  => "  ·  doc: not found",
                DocSyncStatus.NoMapping => string.Empty,
                _ => string.Empty,
            };
        }
        catch (Exception ex) { return $"  ·  doc sync failed: {ex.Message}"; }
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
        RebuildReferenceIndex();
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
        _suppressJsonParse = true;
        try
        {
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
                RawJsonError = string.Empty;
                // Loaded state is the new history baseline; no prior edits are
                // reachable and undo cannot cross into another entity.
                ResetHistory(CurrentEntity?.ToJsonString(SaveJsonOpts) ?? string.Empty);
            }
            catch (Exception ex)
            {
                CurrentEntity = null;
                CurrentSchema = null;
                ResolvedAssets = Array.Empty<ResolvedAsset>();
                RawJson = $"// load failed: {ex.Message}";
                SaveStatus = string.Empty;
                RawJsonError = string.Empty;
                ResetHistory(string.Empty);
            }
        }
        finally { _suppressJsonParse = false; }
        EntityFormChanged?.Invoke();
    }

    private void ClearForm()
    {
        _suppressJsonParse = true;
        try
        {
            CurrentEntity = null;
            CurrentSchema = null;
            ResolvedAssets = Array.Empty<ResolvedAsset>();
            RawJson = string.Empty;
            IsDirty = false;
            SaveStatus = string.Empty;
            RawJsonError = string.Empty;
            ResetHistory(string.Empty);
        }
        finally { _suppressJsonParse = false; }
        EntityFormChanged?.Invoke();
    }

    private int SafeListCount(string typeId)
    {
        try { return _store.List(typeId).Count; }
        catch { return 0; }
    }
}
