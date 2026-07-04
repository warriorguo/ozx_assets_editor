using OAE.Core.Store;
using Xunit.Abstractions;

namespace OAE.Tests;

public class FsStoreTests
{
    private readonly ITestOutputHelper _out;
    public FsStoreTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Mount_throws_when_GameData_dir_missing()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new FsStore("/tmp/oae-no-such-path-xyz"));
    }

    [Fact]
    public void List_returns_descriptors_with_relative_path()
    {
        using var sandbox = NewSandbox();
        File.WriteAllText(Path.Combine(sandbox.GameData, "enemies", "alpha.json"), "{}");
        File.WriteAllText(Path.Combine(sandbox.GameData, "enemies", "beta.json"), "{}");

        var store = new FsStore(sandbox.GameData);
        var list = store.List("enemies");

        Assert.Equal(2, list.Count);
        Assert.Equal(new[] { "alpha", "beta" }, list.Select(e => e.Id));
        Assert.All(list, e => Assert.StartsWith("Assets/StreamingAssets/GameData/enemies/", e.Path));
    }

    [Fact]
    public void Get_returns_raw_bytes_unchanged()
    {
        using var sandbox = NewSandbox();
        const string body = "{\n  \"x\": 1\n}\n";
        File.WriteAllText(Path.Combine(sandbox.GameData, "weapons", "w.json"), body);
        var store = new FsStore(sandbox.GameData);
        Assert.Equal(body, store.Get("weapons", "w"));
    }

    [Fact]
    public void Create_then_Get_then_Delete_round_trip()
    {
        using var sandbox = NewSandbox();
        var store = new FsStore(sandbox.GameData);
        const string body = "{ \"id\": \"new_one\" }\n";
        store.Create("skills", "new_one", body);
        Assert.Equal(body, store.Get("skills", "new_one"));
        store.Delete("skills", "new_one");
        Assert.Throws<EntityNotFoundException>(() => store.Get("skills", "new_one"));
    }

    [Fact]
    public void Update_requires_existing_file()
    {
        using var sandbox = NewSandbox();
        var store = new FsStore(sandbox.GameData);
        Assert.Throws<EntityNotFoundException>(() => store.Update("items", "ghost", "{}"));
    }

    [Fact]
    public void Create_refuses_to_overwrite()
    {
        using var sandbox = NewSandbox();
        var store = new FsStore(sandbox.GameData);
        store.Create("items", "x", "{}\n");
        Assert.Throws<InvalidOperationException>(() => store.Create("items", "x", "{}\n"));
    }

    [Theory]
    [InlineData("BadId")]      // uppercase
    [InlineData("with-dash")]  // dash
    [InlineData("")]           // empty
    [InlineData("a b")]        // space
    public void Create_rejects_invalid_ids(string badId)
    {
        using var sandbox = NewSandbox();
        var store = new FsStore(sandbox.GameData);
        Assert.Throws<ArgumentException>(() => store.Create("items", badId, "{}"));
    }

    [Fact]
    public void Backgrounds_bucket_accepts_mixed_case_asset_name_ids()
    {
        // OAE-48: BackgroundLightData.id mirrors the sprite asset name (PascalCase),
        // so the backgrounds bucket validates ids against the looser asset pattern.
        using var sandbox = NewSandbox();
        var store = new FsStore(sandbox.GameData);
        const string id = "FactoryWall_Big_All_1";
        var body = "{ \"dataType\": \"BackgroundLightData\", \"id\": \"" + id + "\", \"lights\": [] }\n";
        store.Create("backgrounds", id, body);
        Assert.Equal(body, store.Get("backgrounds", id));
        Assert.Equal(id, store.List("backgrounds").Single().Id);
    }

    [Fact]
    public void Non_asset_buckets_still_reject_mixed_case_ids()
    {
        // The looser pattern is scoped to asset-name buckets only.
        using var sandbox = NewSandbox();
        var store = new FsStore(sandbox.GameData);
        Assert.Throws<ArgumentException>(() => store.Create("enemies", "FactoryWall_Big", "{}\n"));
    }

    // ── OAE-32: shared-subdir behaviour (levels ↔ level_plans) ──────────

    [Fact]
    public void List_in_shared_subdir_filters_by_dataType()
    {
        // GameData/levels/ holds both LevelData and LevelBasePlanData post-OZX-443.
        // List("levels") must surface only LevelData files; List("level_plans")
        // must surface only LevelBasePlanData files.
        using var sandbox = NewSandbox();
        var dir = Path.Combine(sandbox.GameData, "levels");
        File.WriteAllText(Path.Combine(dir, "static_a.json"),  "{ \"dataType\": \"LevelData\", \"id\": \"static_a\" }");
        File.WriteAllText(Path.Combine(dir, "static_b.json"),  "{ \"dataType\": \"LevelData\", \"id\": \"static_b\" }");
        File.WriteAllText(Path.Combine(dir, "plan_a.json"),    "{ \"dataType\": \"LevelBasePlanData\", \"id\": \"plan_a\" }");
        File.WriteAllText(Path.Combine(dir, "plan_b.json"),    "{ \"dataType\": \"LevelBasePlanData\", \"id\": \"plan_b\" }");

        var store = new FsStore(sandbox.GameData);

        Assert.Equal(new[] { "static_a", "static_b" },
            store.List("levels").Select(e => e.Id));
        Assert.Equal(new[] { "plan_a", "plan_b" },
            store.List("level_plans").Select(e => e.Id));
    }

    [Fact]
    public void Get_in_shared_subdir_rejects_wrong_dataType()
    {
        // Asking for a LevelData file under entityType "level_plans" must fail
        // with EntityNotFoundException even though the filename exists.
        using var sandbox = NewSandbox();
        var dir = Path.Combine(sandbox.GameData, "levels");
        File.WriteAllText(Path.Combine(dir, "static_a.json"),
            "{ \"dataType\": \"LevelData\", \"id\": \"static_a\" }");

        var store = new FsStore(sandbox.GameData);

        Assert.Equal("static_a", store.List("levels").Single().Id);
        Assert.Throws<EntityNotFoundException>(() => store.Get("level_plans", "static_a"));
    }

    [Fact]
    public void TotalEntityCount_dedupes_shared_subdirs()
    {
        // Without dedup, a file in levels/ would be counted twice (once per
        // entityType key mapping to that subdir).
        using var sandbox = NewSandbox();
        var dir = Path.Combine(sandbox.GameData, "levels");
        File.WriteAllText(Path.Combine(dir, "static_a.json"),
            "{ \"dataType\": \"LevelData\", \"id\": \"static_a\" }");
        File.WriteAllText(Path.Combine(dir, "plan_a.json"),
            "{ \"dataType\": \"LevelBasePlanData\", \"id\": \"plan_a\" }");

        var store = new FsStore(sandbox.GameData);

        Assert.Equal(2, store.TotalEntityCount());
    }

    [Fact]
    public void Subdirs_override_resolves_level_plans_to_levels()
    {
        // Sanity check the OAE-32 override registration itself.
        Assert.Equal("levels", OAE.Core.Schema.EntityTypes.SubdirOf("level_plans"));
        Assert.Equal("levels", OAE.Core.Schema.EntityTypes.SubdirOf("levels"));
        Assert.True(OAE.Core.Schema.EntityTypes.IsSharedSubdir("levels"));
        Assert.True(OAE.Core.Schema.EntityTypes.IsSharedSubdir("level_plans"));
        Assert.False(OAE.Core.Schema.EntityTypes.IsSharedSubdir("enemies"));
    }

    [Fact]
    public void Unknown_type_throws()
    {
        using var sandbox = NewSandbox();
        var store = new FsStore(sandbox.GameData);
        Assert.Throws<ArgumentException>(() => store.List("not_a_real_type"));
    }

    [Fact]
    public void Round_trip_against_ozx_base_is_byte_equivalent()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null)
        {
            _out.WriteLine("ozx_base sibling not found — round-trip test skipped.");
            return;
        }

        var src = new FsStore(Path.Combine(ozx, "Assets", "StreamingAssets", "GameData"));
        using var sandbox = NewSandbox();
        var dst = new FsStore(sandbox.GameData);

        var totalFiles = 0;
        var mismatches = new List<string>();
        foreach (var type in FsStore.Types.Keys)
        {
            foreach (var entity in src.List(type))
            {
                totalFiles++;
                var body = src.Get(type, entity.Id);
                dst.Create(type, entity.Id, body);
                var roundTripped = dst.Get(type, entity.Id);
                if (!string.Equals(body, roundTripped, StringComparison.Ordinal))
                    mismatches.Add($"{type}/{entity.Id}");
            }
        }

        _out.WriteLine($"round-tripped {totalFiles} files across {FsStore.Types.Count} types.");
        Assert.True(totalFiles > 100, $"expected >100 files in ozx_base; got {totalFiles}");
        Assert.Empty(mismatches);
    }

    [Fact]
    public void TotalEntityCount_matches_real_layout_when_sibling_present()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null)
        {
            _out.WriteLine("ozx_base sibling not found — count test skipped.");
            return;
        }
        var store = new FsStore(Path.Combine(ozx, "Assets", "StreamingAssets", "GameData"));
        var n = store.TotalEntityCount();
        _out.WriteLine($"counted {n} entities");
        // Lower bound — content churn in ozx_base (OZX-388/389/390/391 removed
        // multiple files) means an exact count would re-fail on every upstream
        // edit. The test exists to catch missing buckets or a load-path
        // regression, not to track exact population.
        Assert.True(n > 150, $"expected >150 entities in ozx_base; got {n}");
    }

    private static string? ResolveSiblingOzxBase()
    {
        // Walk up from the test assembly looking for a sibling ozx_base/Assets/StreamingAssets/GameData.
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var sibling = Path.Combine(dir, "..", "ozx_base", "Assets", "StreamingAssets", "GameData");
            if (Directory.Exists(sibling)) return Path.GetFullPath(Path.Combine(dir, "..", "ozx_base"));
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static Sandbox NewSandbox() => new();

    private sealed class Sandbox : IDisposable
    {
        public string Root { get; }
        public string GameData { get; }
        public Sandbox()
        {
            Root = Directory.CreateTempSubdirectory("oae-fsstore-").FullName;
            GameData = Path.Combine(Root, "Assets", "StreamingAssets", "GameData");
            Directory.CreateDirectory(GameData);
            // Pre-create every known subdir so List doesn't depend on order of operations.
            foreach (var sub in FsStore.Types.Values)
                Directory.CreateDirectory(Path.Combine(GameData, sub));
        }
        public void Dispose() { try { Directory.Delete(Root, recursive: true); } catch { } }
    }
}
