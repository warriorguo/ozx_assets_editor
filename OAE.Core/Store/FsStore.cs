using System.Text.RegularExpressions;
using OAE.Core.Schema;

namespace OAE.Core.Store;

/// <summary>
/// File-backed <see cref="IStore"/> mounted on
/// <c>&lt;project_root&gt;/Assets/StreamingAssets/GameData/</c>. Each entity
/// type maps to a subdirectory; entity ids match the file basename.
/// Reads return raw UTF-8 bytes and writes pass through verbatim, so the
/// store does not perturb on-disk formatting (key order, indent, trailing
/// newline) — that lets git diffs stay clean and sidesteps Unity-JsonUtility
/// vs System.Text.Json disagreements.
/// </summary>
public sealed class FsStore : IStore
{
    /// <summary>
    /// Maps the OAE-facing entity type id to the on-disk subdirectory under
    /// <c>GameData/</c>. By convention type id == subdir name, derived from
    /// <see cref="EntityTypes.Map"/> so the schema and storage layouts can
    /// never disagree.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Types =
        EntityTypes.Map.Keys.ToDictionary(k => k, k => k);

    private static readonly Regex IdPattern = new("^[a-z0-9_]+$", RegexOptions.Compiled);

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
        var list = new List<EntityDescriptor>();
        foreach (var f in files)
        {
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
        return File.ReadAllText(path);
    }

    public string Create(string entityType, string id, string json)
    {
        ValidateId(id);
        var path = ResolveFile(entityType, id);
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
    /// Total count across every known type. Used by the status banner.
    /// </summary>
    public int TotalEntityCount()
    {
        var total = 0;
        foreach (var t in Types.Keys)
        {
            var dir = ResolveSubdir(t);
            if (Directory.Exists(dir))
                total += Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly).Count();
        }
        return total;
    }

    private string ResolveSubdir(string entityType)
    {
        if (!Types.TryGetValue(entityType, out var subdir))
            throw new ArgumentException($"unknown entity type: {entityType}", nameof(entityType));
        return Path.Combine(_gameDataDir, subdir);
    }

    private string ResolveFile(string entityType, string id)
    {
        ValidateId(id);
        return Path.Combine(ResolveSubdir(entityType), id + ".json");
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || !IdPattern.IsMatch(id))
            throw new ArgumentException($"invalid id (expected [a-z0-9_]+): {id}", nameof(id));
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
