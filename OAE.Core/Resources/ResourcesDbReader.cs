using System.Text.RegularExpressions;

namespace OAE.Core.Resources;

/// <summary>
/// Read-only parser for <c>Assets/Prefabs/System/ResourcesDB.asset</c> — the
/// Unity ScriptableObject that maps human keys (e.g. <c>anim/arack_orange</c>)
/// to asset GUIDs. Combined with <see cref="UnityMetaIndex"/> this gives the
/// editor a direct <c>key -&gt; file path</c> resolution.
/// </summary>
/// <remarks>
/// The full editor for ResourcesDB lives in OAE-10. Here we only read.
/// </remarks>
public sealed class ResourcesDbReader
{
    private static readonly Regex EntryPattern = new(
        @"-\s*key:\s*(?<key>[^\r\n]+?)\s*\r?\n\s*asset:\s*\{[^}]*guid:\s*(?<guid>[a-f0-9]{32})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dictionary<string, string> _keyToGuid = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> KeyToGuid => _keyToGuid;
    public int EntryCount => _keyToGuid.Count;

    /// <summary>
    /// Default location of the ScriptableObject within an ozx_base checkout.
    /// </summary>
    public static string DefaultPathFor(string projectRoot) =>
        Path.Combine(projectRoot, "Assets", "Prefabs", "System", "ResourcesDB.asset");

    /// <summary>
    /// Parse the file at <paramref name="path"/>. Idempotent — call again to
    /// pick up edits made outside OAE.
    /// </summary>
    public void Load(string path)
    {
        _keyToGuid.Clear();
        if (!File.Exists(path)) return;
        var text = File.ReadAllText(path);
        foreach (Match m in EntryPattern.Matches(text))
        {
            var key = m.Groups["key"].Value.Trim();
            var guid = m.Groups["guid"].Value.ToLowerInvariant();
            _keyToGuid[key] = guid;
        }
    }

    public string? GuidFor(string key) =>
        _keyToGuid.TryGetValue(key, out var g) ? g : null;
}
