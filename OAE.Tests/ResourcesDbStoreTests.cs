using OAE.Core.Resources;
using Xunit.Abstractions;

namespace OAE.Tests;

public class ResourcesDbStoreTests
{
    private readonly ITestOutputHelper _out;
    public ResourcesDbStoreTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Load_then_List_matches_reader_count()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var dbPath = ResourcesDbStore.DefaultPathFor(ozx);
        var store = new ResourcesDbStore();
        store.Load(dbPath);
        var entries = store.List();
        _out.WriteLine($"entries: {entries.Count}");
        Assert.True(entries.Count > 100, "expected > 100 entries in real ResourcesDB");
        // Spot check
        var arack = store.Get("anim/arack_orange");
        Assert.NotNull(arack);
        Assert.Equal("eefd409a84c404fc08e9f599c60393fa", arack!.Guid);
        Assert.Equal(2, arack.Type);
    }

    [Fact]
    public void Add_then_Remove_round_trips_bytes()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var src = ResourcesDbStore.DefaultPathFor(ozx);
        var sandboxDir = Directory.CreateTempSubdirectory("oae-rdb-").FullName;
        try
        {
            var sandboxPath = Path.Combine(sandboxDir, "ResourcesDB.asset");
            File.Copy(src, sandboxPath);
            var originalBytes = File.ReadAllBytes(sandboxPath);

            var store = new ResourcesDbStore();
            store.Load(sandboxPath);

            const string testKey = "oae10/round_trip_test_key";
            const string testGuid = "0123456789abcdef0123456789abcdef";
            store.Add(testKey, testGuid, fileId: 21300000, type: 3);

            // Reload to confirm persistence.
            var reload = new ResourcesDbStore();
            reload.Load(sandboxPath);
            var entry = reload.Get(testKey);
            Assert.NotNull(entry);
            Assert.Equal(testGuid, entry!.Guid);
            Assert.Equal(3, entry.Type);

            // Now remove the entry and assert the file matches the original
            // byte-for-byte — the round trip should be neutral.
            store.Remove(testKey);
            var afterRemove = File.ReadAllBytes(sandboxPath);
            Assert.Equal(originalBytes.Length, afterRemove.Length);
            Assert.True(originalBytes.AsSpan().SequenceEqual(afterRemove),
                "byte-equivalent round trip expected");
        }
        finally { Directory.Delete(sandboxDir, recursive: true); }
    }

    [Fact]
    public void Add_duplicate_throws()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var src = ResourcesDbStore.DefaultPathFor(ozx);
        var sandboxDir = Directory.CreateTempSubdirectory("oae-rdb-dup-").FullName;
        try
        {
            var sandboxPath = Path.Combine(sandboxDir, "ResourcesDB.asset");
            File.Copy(src, sandboxPath);
            var store = new ResourcesDbStore();
            store.Load(sandboxPath);

            Assert.Throws<InvalidOperationException>(() =>
                store.Add("anim/arack_orange", "ffffffffffffffffffffffffffffffff", 21300000, 3));
        }
        finally { Directory.Delete(sandboxDir, recursive: true); }
    }

    [Fact]
    public void Update_replaces_existing_entry()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var src = ResourcesDbStore.DefaultPathFor(ozx);
        var sandboxDir = Directory.CreateTempSubdirectory("oae-rdb-update-").FullName;
        try
        {
            var sandboxPath = Path.Combine(sandboxDir, "ResourcesDB.asset");
            File.Copy(src, sandboxPath);

            var store = new ResourcesDbStore();
            store.Load(sandboxPath);
            const string testKey = "anim/arack_orange";
            const string newGuid = "1111111111111111111111111111ffff";
            store.Update(testKey, newGuid, fileId: 11400000, type: 2);

            var reload = new ResourcesDbStore();
            reload.Load(sandboxPath);
            Assert.Equal(newGuid, reload.Get(testKey)!.Guid);
        }
        finally { Directory.Delete(sandboxDir, recursive: true); }
    }

    private static string? ResolveSiblingOzxBase()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var sibling = Path.Combine(dir, "..", "ozx_base", "Assets", "StreamingAssets", "GameData");
            if (Directory.Exists(sibling)) return Path.GetFullPath(Path.Combine(dir, "..", "ozx_base"));
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
