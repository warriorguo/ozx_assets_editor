using System.Text.RegularExpressions;

namespace OAE.Core.Resources;

/// <summary>
/// Facade over <see cref="ResourcesDbReader"/> + <see cref="UnityMetaIndex"/>:
/// resolves a ResourcesDB key (e.g. <c>anim/arack_orange</c>) to a viewable
/// PNG path when one exists, falling back to whatever asset the DB points at
/// when no image can be found.
/// </summary>
public sealed class AssetLocator
{
    private static readonly Regex GuidLine = new(@"guid:\s*([a-f0-9]{32})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ResourcesDbReader Db { get; }
    public UnityMetaIndex Meta { get; }
    public string ProjectRoot { get; }

    public AssetLocator(string projectRoot)
    {
        ProjectRoot = projectRoot;
        Db = new ResourcesDbReader();
        Meta = new UnityMetaIndex(Path.Combine(projectRoot, "Assets"));
    }

    /// <summary>
    /// Build / refresh both indexes. Returns <c>true</c> if the ResourcesDB
    /// asset existed and was parsed.
    /// </summary>
    public bool Build()
    {
        Meta.Build();
        var dbPath = ResourcesDbReader.DefaultPathFor(ProjectRoot);
        Db.Load(dbPath);
        return File.Exists(dbPath);
    }

    /// <summary>
    /// Outcome of <see cref="Resolve"/>: the PNG to render plus a hop count
    /// (0 = direct, 1+ = drilled through one or more <c>.asset</c> files).
    /// <see cref="DirectAssetPath"/> is the file the DB key points at, useful
    /// for pipelines that need to overwrite that file specifically.
    /// </summary>
    public sealed record Located(string? ImagePath, string? DirectAssetPath, int Hops);

    public Located Resolve(string key)
    {
        var guid = Db.GuidFor(key);
        if (guid is null) return new Located(null, null, 0);
        var direct = Meta.PathFor(guid);
        if (direct is null) return new Located(null, null, 0);

        if (IsImage(direct)) return new Located(direct, direct, 0);

        // Asset is e.g. a SpriteAnimationData / CharacterAnimConfig that
        // references the actual sprites by GUID inside its YAML body. Walk
        // those references and surface the first one that resolves to an image.
        if (TryReadFirstImageFromAssetGuids(direct, out var image, out var hops))
            return new Located(image, direct, hops);

        return new Located(null, direct, 0);
    }

    private bool TryReadFirstImageFromAssetGuids(string assetPath, out string? imagePath, out int hops)
    {
        imagePath = null;
        hops = 0;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { assetPath };
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((assetPath, 1));

        const int maxDepth = 3;
        const int maxNodes = 32;
        var seenNodes = 0;

        while (queue.Count > 0 && seenNodes < maxNodes)
        {
            var (current, depth) = queue.Dequeue();
            seenNodes++;
            string body;
            try { body = File.ReadAllText(current); }
            catch { continue; }

            foreach (Match m in GuidLine.Matches(body))
            {
                var refGuid = m.Groups[1].Value.ToLowerInvariant();
                var refPath = Meta.PathFor(refGuid);
                if (refPath is null || !visited.Add(refPath)) continue;
                if (IsImage(refPath))
                {
                    imagePath = refPath;
                    hops = depth;
                    return true;
                }
                if (depth < maxDepth) queue.Enqueue((refPath, depth + 1));
            }
        }
        return false;
    }

    private static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }
}
