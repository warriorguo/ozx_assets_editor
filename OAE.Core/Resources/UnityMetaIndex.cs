using System.Text.RegularExpressions;

namespace OAE.Core.Resources;

/// <summary>
/// Builds a lookup of <c>guid -&gt; absolute asset path</c> by walking every
/// <c>*.meta</c> file under <c>Assets/</c> and grabbing the <c>guid:</c> line.
/// One pass per project mount; cached in-memory because Unity guids are stable
/// per asset for the life of the project.
/// </summary>
public sealed class UnityMetaIndex
{
    private static readonly Regex GuidLine = new(@"^guid:\s*([a-f0-9]{32})", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private readonly Dictionary<string, string> _guidToPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _assetsRoot;

    public UnityMetaIndex(string assetsRoot)
    {
        _assetsRoot = assetsRoot;
    }

    public string AssetsRoot => _assetsRoot;
    public int Count => _guidToPath.Count;

    /// <summary>
    /// Walks <see cref="AssetsRoot"/> for <c>*.meta</c> files and indexes them.
    /// Subsequent calls clear and rebuild — cheap, so callers can refresh after
    /// an import that may have added new assets.
    /// </summary>
    public void Build()
    {
        _guidToPath.Clear();
        if (!Directory.Exists(_assetsRoot)) return;

        foreach (var meta in Directory.EnumerateFiles(_assetsRoot, "*.meta", SearchOption.AllDirectories))
        {
            string firstChunk;
            try { firstChunk = ReadFirst(meta, 256); }
            catch { continue; }
            var match = GuidLine.Match(firstChunk);
            if (!match.Success) continue;
            var guid = match.Groups[1].Value.ToLowerInvariant();
            // Strip the trailing ".meta" — the asset path is the rest.
            var assetPath = meta[..^".meta".Length];
            _guidToPath[guid] = assetPath;
        }
    }

    public string? PathFor(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        return _guidToPath.TryGetValue(guid.ToLowerInvariant(), out var p) ? p : null;
    }

    private static string ReadFirst(string path, int maxBytes)
    {
        using var stream = File.OpenRead(path);
        Span<byte> buf = stackalloc byte[maxBytes];
        var n = stream.Read(buf);
        return System.Text.Encoding.UTF8.GetString(buf[..n]);
    }
}
