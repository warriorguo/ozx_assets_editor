using System.Text.Json;
using System.Text.RegularExpressions;
using OAE.Core.Schema;

namespace OAE.Core.Store;

/// <summary>
/// File-backed <see cref="IStore"/> mounted on
/// <c>&lt;project_root&gt;/Assets/StreamingAssets/GameData/</c>. Each entity
/// type maps to a subdirectory (possibly shared with other entity types,
/// see <see cref="EntityTypes.Subdirs"/>); entity ids match the file basename.
/// Reads return raw UTF-8 bytes and writes pass through verbatim, so the
/// store does not perturb on-disk formatting (key order, indent, trailing
/// newline) — that lets git diffs stay clean and sidesteps Unity-JsonUtility
/// vs System.Text.Json disagreements.
/// </summary>
public sealed class FsStore : IStore
{
    /// <summary>
    /// Maps the OAE-facing entity type id to the on-disk subdirectory under
    /// <c>GameData/</c>. Derived from <see cref="EntityTypes.Map"/> via
    /// <see cref="EntityTypes.SubdirOf"/>; defaults to type id == subdir name,
    /// with overrides (OAE-32) for shared subdirs.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Types =
        EntityTypes.Map.Keys.ToDictionary(k => k, EntityTypes.SubdirOf);

    private static readonly Regex IdPattern = new("^[a-z0-9_]+$", RegexOptions.Compiled);
    // OAE-48: asset-name-id buckets (e.g. backgrounds) carry mixed-case ids that
    // mirror a sprite asset name — validate them against a looser pattern.
    private static readonly Regex AssetIdPattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    private readonly string _gameDataDir;
    private readonly string _projectRoot;
    private readonly Lock _lock = new();

    /// <summary>
    /// Mount on <paramref name="gameDataDir"/>. Throws if the directory
    /// doesn't exist — callers should fall back to <see cref="StubStore"/>.
    /// </summary>
    public FsStore(string gameDataDir)
    {
        if (!Directory.Exists(gameDataDir))
            throw new DirectoryNotFoundException($"GameData dir not found: {gameDataDir}");
        _gameDataDir = gameDataDir;
        // Project root sits two levels above GameData/ (Assets/StreamingAssets/GameData)
        _projectRoot = Path.GetFullPath(Path.Combine(gameDataDir, "..", "..", ".."));
    }

    public string GameDataDir => _gameDataDir;
    public string ProjectRoot => _projectRoot;

    public StoreHealth HealthCheck() => new(Ok: true, Root: _projectRoot);

    public IReadOnlyList<EntityDescriptor> List(string entityType)
    {
        var dir = ResolveSubdir(entityType);
        if (!Directory.Exists(dir)) return Array.Empty<EntityDescriptor>();
        var files = Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly);
        var shared = EntityTypes.IsSharedSubdir(entityType);
        var expectedDataType = shared ? EntityTypes.DataTypeOf(entityType) : null;
        var list = new List<EntityDescriptor>();
        foreach (var f in files)
        {
            if (shared && PeekDataType(f) != expectedDataType) continue;
            var id = Path.GetFileNameWithoutExtension(f);
            list.Add(new EntityDescriptor(entityType, id, Path.GetRelativePath(_projectRoot, f)));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return list;
    }

    public string Get(string entityType, string id)
    {
        var path = ResolveFile(entityType, id);
        if (!File.Exists(path)) throw new EntityNotFoundException(entityType, id);
        // On a shared subdir, the filename alone does not disambiguate which
        // entity type the file belongs to — validate via dataType (OAE-32).
        if (EntityTypes.IsSharedSubdir(entityType)
            && PeekDataType(path) != EntityTypes.DataTypeOf(entityType))
            throw new EntityNotFoundException(entityType, id);
        return File.ReadAllText(path);
    }

    public string Create(string entityType, string id, string json)
    {
        var path = ResolveFile(entityType, id); // ResolveFile validates the id
        lock (_lock)
        {
            if (File.Exists(path))
                throw new InvalidOperationException($"entity already exists: {entityType}/{id}");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicWrite(path, json);
        }
        return json;
    }

    public string Update(string entityType, string id, string json)
    {
        var path = ResolveFile(entityType, id);
        lock (_lock)
        {
            if (!File.Exists(path)) throw new EntityNotFoundException(entityType, id);
            AtomicWrite(path, json);
        }
        return json;
    }

    public void Delete(string entityType, string id)
    {
        var path = ResolveFile(entityType, id);
        lock (_lock)
        {
            if (!File.Exists(path)) throw new EntityNotFoundException(entityType, id);
            File.Delete(path);
        }
    }

    /// <summary>
    /// Total count across every known type. Used by the status banner. Subdirs
    /// shared across entity types (OAE-32) are counted once via deduplication.
    /// </summary>
    public int TotalEntityCount()
    {
        var total = 0;
        var seen = new HashSet<string>();
        foreach (var t in Types.Keys)
        {
            var dir = ResolveSubdir(t);
            if (!seen.Add(dir)) continue;
            if (Directory.Exists(dir))
                total += Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly).Count();
        }
        return total;
    }

    /// <summary>
    /// OAE-32: peeks the <c>dataType</c> field from a JSON file without fully
    /// parsing it. Returns null when the field is absent or the file cannot be
    /// parsed — callers should treat null as "does not match" so unrelated
    /// files in a shared subdir are simply skipped.
    /// </summary>
    private static string? PeekDataType(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            return doc.RootElement.TryGetProperty("dataType", out var dt) && dt.ValueKind == JsonValueKind.String
                ? dt.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private string ResolveSubdir(string entityType)
    {
        if (!Types.TryGetValue(entityType, out var subdir))
            throw new ArgumentException($"unknown entity type: {entityType}", nameof(entityType));
        return Path.Combine(_gameDataDir, subdir);
    }

    private string ResolveFile(string entityType, string id)
    {
        ValidateId(entityType, id);
        return Path.Combine(ResolveSubdir(entityType), id + ".json");
    }

    private static void ValidateId(string entityType, string id)
    {
        var assetName = EntityTypes.UsesAssetNameId(entityType);
        var pattern = assetName ? AssetIdPattern : IdPattern;
        if (string.IsNullOrEmpty(id) || !pattern.IsMatch(id))
            throw new ArgumentException(
                $"invalid id (expected {(assetName ? "[A-Za-z0-9_]+" : "[a-z0-9_]+")}): {id}",
                nameof(id));
    }

    private static void AtomicWrite(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>
/// Helper used by <c>MainWindowViewModel</c> to decide whether to swap in a
/// real <see cref="FsStore"/> or fall back to a <see cref="StubStore"/>.
/// </summary>
public static class StoreFactory
{
    public static IStore CreateForProject(string? gameDataDir)
    {
        if (string.IsNullOrEmpty(gameDataDir) || !Directory.Exists(gameDataDir))
            return new StubStore("GameData/ not found under project_root");
        try { return new FsStore(gameDataDir); }
        catch (Exception ex) { return new StubStore($"FsStore mount failed: {ex.Message}"); }
    }
}
