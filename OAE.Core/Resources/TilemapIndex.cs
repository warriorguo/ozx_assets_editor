using System.Text.Json;

namespace OAE.Core.Resources;

/// <summary>
/// One tilemap on disk under <c>Assets/StreamingAssets/TilemapData/&lt;theme&gt;/&lt;stem&gt;.json</c>.
/// Metadata is read lazily — listing 257+ tilemaps shouldn't fan out into
/// 257 file reads on window open.
/// </summary>
public sealed class TilemapEntry
{
    public string Theme { get; }
    public string Stem { get; }
    public string FullPath { get; }
    public long SizeBytes { get; }

    /// <summary>
    /// "&lt;theme&gt;/&lt;stem&gt;" — useful both as a display label and as the
    /// clipboard payload for the "Edit in ORT" handoff.
    /// </summary>
    public string Key => $"{Theme}/{Stem}";

    private TilemapDocument? _doc;

    public TilemapEntry(string theme, string stem, string fullPath, long sizeBytes)
    {
        Theme = theme;
        Stem = stem;
        FullPath = fullPath;
        SizeBytes = sizeBytes;
    }

    public TilemapDocument Document => _doc ??= TilemapReader.Read(FullPath);
    public void Invalidate() => _doc = null;
}

/// <summary>
/// Discovers all tilemaps under <c>&lt;projectRoot&gt;/Assets/StreamingAssets/TilemapData/</c>
/// and exposes filtering helpers used by the browser window. A separate
/// helper resolves reverse references against the <c>levels/</c> entity bucket
/// (rooms reference tilemaps via <c>floors[].rooms[].templateId</c> in
/// <see cref="Game.Contracts.Data.LevelData"/>).
/// </summary>
public sealed class TilemapIndex
{
    public static readonly string[] KnownThemes = { "normal", "cave", "basement", "test" };

    public string ProjectRoot { get; }
    public string TilemapDataRoot { get; }
    public IReadOnlyList<TilemapEntry> Entries { get; }

    public TilemapIndex(string projectRoot)
    {
        ProjectRoot = projectRoot;
        TilemapDataRoot = Path.Combine(projectRoot, "Assets", "StreamingAssets", "TilemapData");
        Entries = Discover(TilemapDataRoot);
    }

    private static IReadOnlyList<TilemapEntry> Discover(string root)
    {
        var entries = new List<TilemapEntry>();
        if (!Directory.Exists(root)) return entries;

        foreach (var theme in KnownThemes)
        {
            var themeDir = Path.Combine(root, theme);
            if (!Directory.Exists(themeDir)) continue;

            foreach (var file in Directory.EnumerateFiles(themeDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                var size = new FileInfo(file).Length;
                entries.Add(new TilemapEntry(theme, stem, file, size));
            }
        }

        entries.Sort((a, b) =>
        {
            var cmp = string.CompareOrdinal(a.Theme, b.Theme);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Stem, b.Stem);
        });
        return entries;
    }

    public IEnumerable<TilemapEntry> ByTheme(string theme) =>
        Entries.Where(e => e.Theme == theme);

    /// <summary>
    /// Walks the <c>levels/</c> entity bucket and returns the level ids that
    /// reference a tilemap with the given stem via
    /// <c>floors[].rooms[].templateId</c>. Stem-only match (the levels JSON
    /// doesn't include the theme prefix).
    /// </summary>
    public IReadOnlyList<string> FindReverseRefs(string stem)
    {
        var levelsDir = Path.Combine(ProjectRoot, "Assets", "StreamingAssets", "GameData", "levels");
        if (!Directory.Exists(levelsDir)) return Array.Empty<string>();

        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(levelsDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }

            // Fast prefilter — avoid parsing JSON for every level on miss.
            if (!text.Contains(stem, StringComparison.Ordinal)) continue;

            string? id = null;
            var matched = false;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("id", out var idEl))
                    id = idEl.GetString();
                if (doc.RootElement.TryGetProperty("floors", out var floorsEl) && floorsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var floor in floorsEl.EnumerateArray())
                    {
                        if (!floor.TryGetProperty("rooms", out var roomsEl) || roomsEl.ValueKind != JsonValueKind.Array) continue;
                        foreach (var room in roomsEl.EnumerateArray())
                        {
                            if (room.TryGetProperty("templateId", out var tEl)
                                && tEl.ValueKind == JsonValueKind.String
                                && string.Equals(tEl.GetString(), stem, StringComparison.Ordinal))
                            {
                                matched = true;
                                break;
                            }
                        }
                        if (matched) break;
                    }
                }
            }
            catch { /* skip malformed level files */ }

            if (matched) hits.Add(id ?? Path.GetFileNameWithoutExtension(file));
        }

        hits.Sort(StringComparer.Ordinal);
        return hits;
    }
}
