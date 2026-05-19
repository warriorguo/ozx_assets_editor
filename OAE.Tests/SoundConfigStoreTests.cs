using OAE.Core.Resources;
using Xunit.Abstractions;

namespace OAE.Tests;

public class SoundConfigStoreTests
{
    private readonly ITestOutputHelper _out;
    public SoundConfigStoreTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void List_against_sibling_ozx_base_reads_expected_entries()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var store = new SoundConfigStore();
        store.Load(SoundConfigStore.DefaultPathFor(ozx));
        var entries = store.List();
        _out.WriteLine($"entries: {entries.Count}");
        Assert.True(entries.Count >= 10, "expected ≥10 SFX entries");

        // Spot-check the first known entry (fires the shotgun).
        var shotgun = entries.FirstOrDefault(e => e.SoundId == "sfx_shotgun_fire");
        Assert.NotNull(shotgun);
        Assert.True(shotgun!.ClipCount > 0, "sfx_shotgun_fire has a clip");
        Assert.InRange(shotgun.Volume, 0.0f, 2.0f);
    }

    [Fact]
    public void Remove_then_reload_round_trips_unrelated_bytes()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var src = SoundConfigStore.DefaultPathFor(ozx);
        var sandboxDir = Directory.CreateTempSubdirectory("oae-sc-").FullName;
        try
        {
            var sandboxPath = Path.Combine(sandboxDir, "SoundConfig.asset");
            File.Copy(src, sandboxPath);

            var store = new SoundConfigStore();
            store.Load(sandboxPath);
            var before = store.List();
            Assert.Contains(before, e => e.SoundId == "sfx_player_hit");

            Assert.True(store.Remove("sfx_player_hit"));

            var reload = new SoundConfigStore();
            reload.Load(sandboxPath);
            var after = reload.List();
            Assert.Equal(before.Count - 1, after.Count);
            Assert.DoesNotContain(after, e => e.SoundId == "sfx_player_hit");
            // Verify a different known entry is still there + unchanged.
            var shotgun = after.FirstOrDefault(e => e.SoundId == "sfx_shotgun_fire");
            Assert.NotNull(shotgun);
            var srcShotgun = before.First(e => e.SoundId == "sfx_shotgun_fire");
            Assert.Equal(srcShotgun.Volume, shotgun!.Volume);
            Assert.Equal(srcShotgun.ClipCount, shotgun.ClipCount);
        }
        finally { Directory.Delete(sandboxDir, recursive: true); }
    }

    [Fact]
    public void Remove_unknown_returns_false_no_change()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var src = SoundConfigStore.DefaultPathFor(ozx);
        var sandboxDir = Directory.CreateTempSubdirectory("oae-sc-nochg-").FullName;
        try
        {
            var sandboxPath = Path.Combine(sandboxDir, "SoundConfig.asset");
            File.Copy(src, sandboxPath);
            var originalBytes = File.ReadAllBytes(sandboxPath);
            var store = new SoundConfigStore();
            store.Load(sandboxPath);
            Assert.False(store.Remove("sfx_no_such_sound_xyz"));
            var afterBytes = File.ReadAllBytes(sandboxPath);
            Assert.True(originalBytes.AsSpan().SequenceEqual(afterBytes));
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
