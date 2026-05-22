using OAE.Core.Resources;
using Xunit.Abstractions;

namespace OAE.Tests;

public class TilemapIndexTests
{
    private readonly ITestOutputHelper _out;
    public TilemapIndexTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Reader_parses_a_real_tilemap_round_trip()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var dir = Path.Combine(ozx, "Assets", "StreamingAssets", "TilemapData", "normal");
        var sample = Directory.EnumerateFiles(dir, "*.json").FirstOrDefault();
        Assert.NotNull(sample);

        var doc = TilemapReader.Read(sample!);
        Assert.NotNull(doc);
        Assert.NotNull(doc.Ground);
        Assert.True(doc.Height > 0 && doc.Width > 0, $"{Path.GetFileName(sample)}: expected non-zero size");
        Assert.NotEmpty(doc.Ground!);
        Assert.Equal(doc.Height, doc.Ground!.Length);
        Assert.Equal(doc.Width, doc.Ground![0].Length);
        Assert.False(string.IsNullOrEmpty(doc.RoomCategory));
    }

    [Fact]
    public void Reader_pulls_metadata_doors_and_stage_fields()
    {
        const string json = """
        {
          "ground": [[1,1],[1,1]],
          "doors": { "left": 0, "top": 1, "right": 1, "bottom": 0 },
          "stageType": "boss",
          "roomShape": "all",
          "roomCategory": "normal",
          "openDoors": 3,
          "meta": { "name": "full-2x2", "version": 1, "width": 2, "height": 2 }
        }
        """;
        var doc = TilemapReader.Parse(json);
        Assert.Equal("boss", doc.StageType);
        Assert.Equal("all", doc.RoomShape);
        Assert.Equal("normal", doc.RoomCategory);
        Assert.Equal(3, doc.OpenDoors);
        Assert.Equal(1, doc.Doors.Top);
        Assert.Equal(1, doc.Doors.Right);
        Assert.Equal(0, doc.Doors.Left);
        Assert.Equal(0, doc.Doors.Bottom);
        Assert.Equal("full-2x2", doc.Meta.Name);
        Assert.Equal(2, doc.Width);
        Assert.Equal(2, doc.Height);
    }

    [Fact]
    public void Index_enumerates_normal_bucket_against_sibling_ozx_base()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var idx = new TilemapIndex(ozx);
        var normal = idx.ByTheme("normal").ToList();
        _out.WriteLine($"normal bucket size: {normal.Count}");
        Assert.True(normal.Count >= 200, $"expected ≥200 normal tilemaps, got {normal.Count}");
        Assert.All(normal, e => Assert.True(e.SizeBytes > 0));
        Assert.All(normal, e => Assert.False(string.IsNullOrEmpty(e.Stem)));
        // Entries are sorted by (theme, stem).
        Assert.Equal(
            normal.Select(e => e.Stem).Order(StringComparer.Ordinal),
            normal.Select(e => e.Stem));
    }

    [Fact]
    public void Index_reverse_refs_resolve_at_least_one_real_level_match()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var idx = new TilemapIndex(ozx);
        var hitForAny = idx.Entries
            .Take(80) // bounded — finding one match is enough
            .Select(e => (e.Stem, refs: idx.FindReverseRefs(e.Stem)))
            .FirstOrDefault(t => t.refs.Count > 0);
        _out.WriteLine($"sample match: stem={hitForAny.Stem}, refs={string.Join(',', hitForAny.refs ?? Array.Empty<string>())}");
        Assert.NotNull(hitForAny.refs);
        // Either there's no level data, or we found at least one rev-ref.
        // The "no levels" case is handled by FindReverseRefs returning empty.
    }

    [Fact]
    public void Index_reverse_refs_returns_empty_for_unknown_stem()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var idx = new TilemapIndex(ozx);
        var refs = idx.FindReverseRefs("definitely_not_a_real_tilemap_name");
        Assert.Empty(refs);
    }

    private static string? ResolveSiblingOzxBase()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var sibling = Path.Combine(dir, "..", "ozx_base", "Assets", "StreamingAssets", "TilemapData");
            if (Directory.Exists(sibling)) return Path.GetFullPath(Path.Combine(dir, "..", "ozx_base"));
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
