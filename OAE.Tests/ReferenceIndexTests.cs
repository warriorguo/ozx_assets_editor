using OAE.Core.References;
using OAE.Core.Store;
using Xunit.Abstractions;

namespace OAE.Tests;

public class ReferenceIndexTests
{
    private readonly ITestOutputHelper _out;
    public ReferenceIndexTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Rebuild_from_StubStore_yields_empty_sets()
    {
        var index = new ReferenceIndex();
        index.Rebuild(new StubStore("test"));
        Assert.Empty(index.IdsOf("enemies"));
        Assert.False(index.Contains("enemies", "anything"));
    }

    [Fact]
    public void Unknown_type_returns_empty_set()
    {
        var index = new ReferenceIndex();
        index.Rebuild(new StubStore());
        Assert.Empty(index.IdsOf("not_a_real_type"));
    }

    [Fact]
    public void Rebuild_against_sibling_ozx_base_finds_known_ids()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var store = new FsStore(System.IO.Path.Combine(ozx, "Assets", "StreamingAssets", "GameData"));
        var index = new ReferenceIndex();
        index.Rebuild(store);

        // Spot-check: known existing ids per type.
        Assert.True(index.Contains("enemies", "arack_orange"),
            "arack_orange should be in the enemies index");
        Assert.True(index.Contains("projectiles", "bullet"),
            "bullet should be in the projectiles index");
        Assert.True(index.Contains("loot_tables", "common_enemy"),
            "common_enemy should be in the loot_tables index");
        Assert.False(index.Contains("enemies", "definitely_not_an_enemy_id"),
            "bogus id should not be present");

        _out.WriteLine($"enemies: {index.IdsOf("enemies").Count}, projectiles: {index.IdsOf("projectiles").Count}, loot_tables: {index.IdsOf("loot_tables").Count}");
    }

    private static string? ResolveSiblingOzxBase()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var sibling = System.IO.Path.Combine(dir, "..", "ozx_base", "Assets", "StreamingAssets", "GameData");
            if (System.IO.Directory.Exists(sibling)) return System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, "..", "ozx_base"));
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        return null;
    }
}
