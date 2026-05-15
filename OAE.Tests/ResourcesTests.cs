using System.Buffers.Binary;
using System.Text.Json.Nodes;
using OAE.Core.Resources;
using OAE.Core.Schema;
using Game.Contracts.Data;
using Xunit.Abstractions;

namespace OAE.Tests;

public class ResourcesTests
{
    private readonly ITestOutputHelper _out;
    public ResourcesTests(ITestOutputHelper output) => _out = output;

    // ── PngReader ────────────────────────────────────────────────────────

    [Fact]
    public void PngReader_reads_dimensions_from_header()
    {
        var path = WriteFakePng(width: 256, height: 64);
        try
        {
            var dims = PngReader.TryReadDimensions(path);
            Assert.NotNull(dims);
            Assert.Equal(256, dims!.Value.Width);
            Assert.Equal(64, dims.Value.Height);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PngReader_returns_null_for_non_png()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oae-not-a-png-{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "this is just text");
        try
        {
            Assert.Null(PngReader.TryReadDimensions(path));
        }
        finally { File.Delete(path); }
    }

    // ── ResourcesDbReader ────────────────────────────────────────────────

    [Fact]
    public void ResourcesDbReader_parses_entries()
    {
        const string yaml = """
%YAML 1.1
MonoBehaviour:
  m_Name: ResourcesDB
  _entries:
  - key: anim/arack_orange
    asset: {fileID: 11400000, guid: eefd409a84c404fc08e9f599c60393fa, type: 2}
  - key: skill/attack_boost@icon
    asset: {fileID: 21300000, guid: a7bfd697f067a4ddab82883144b45343, type: 3}
""";
        var path = Path.Combine(Path.GetTempPath(), $"oae-rdb-{Guid.NewGuid():N}.asset");
        File.WriteAllText(path, yaml);
        try
        {
            var rdb = new ResourcesDbReader();
            rdb.Load(path);
            Assert.Equal(2, rdb.EntryCount);
            Assert.Equal("eefd409a84c404fc08e9f599c60393fa", rdb.GuidFor("anim/arack_orange"));
            Assert.Equal("a7bfd697f067a4ddab82883144b45343", rdb.GuidFor("skill/attack_boost@icon"));
        }
        finally { File.Delete(path); }
    }

    // ── UnityMetaIndex + AssetLocator (synthetic) ────────────────────────

    [Fact]
    public void AssetLocator_resolves_key_to_png_via_synthetic_db()
    {
        var sandbox = Directory.CreateTempSubdirectory("oae-locator-").FullName;
        try
        {
            var assets = Path.Combine(sandbox, "Assets");
            var imagesDir = Path.Combine(assets, "Images", "Items");
            var dbDir = Path.Combine(assets, "Prefabs", "System");
            Directory.CreateDirectory(imagesDir);
            Directory.CreateDirectory(dbDir);

            var pngPath = Path.Combine(imagesDir, "Icon.png");
            File.WriteAllBytes(pngPath, FakePngBytes(64, 64));
            const string guid = "abcd0123abcd0123abcd0123abcd0123";
            File.WriteAllText(pngPath + ".meta", $"fileFormatVersion: 2\nguid: {guid}\nTextureImporter: {{}}\n");

            var dbPath = Path.Combine(dbDir, "ResourcesDB.asset");
            File.WriteAllText(dbPath,
                "MonoBehaviour:\n  _entries:\n  - key: item/icon\n    asset: {fileID: 21300000, guid: " + guid + ", type: 3}\n");

            var loc = new AssetLocator(sandbox);
            Assert.True(loc.Build());
            var hit = loc.Resolve("item/icon");
            Assert.Equal(pngPath, hit.ImagePath);
            Assert.Equal(0, hit.Hops);
        }
        finally { Directory.Delete(sandbox, recursive: true); }
    }

    // ── AssetResolver against synthetic enemy ────────────────────────────

    [Fact]
    public void AssetResolver_walks_enemy_animConfigKey_field()
    {
        var sandbox = Directory.CreateTempSubdirectory("oae-resolver-").FullName;
        try
        {
            var assets = Path.Combine(sandbox, "Assets");
            var charsDir = Path.Combine(assets, "Images", "Characters", "Test");
            var dbDir = Path.Combine(assets, "Prefabs", "System");
            Directory.CreateDirectory(charsDir);
            Directory.CreateDirectory(dbDir);

            var pngPath = Path.Combine(charsDir, "Test.png");
            File.WriteAllBytes(pngPath, FakePngBytes(128, 32));
            const string guid = "0011223344556677889900aabbccddee";
            File.WriteAllText(pngPath + ".meta", $"fileFormatVersion: 2\nguid: {guid}\n");

            var dbPath = Path.Combine(dbDir, "ResourcesDB.asset");
            File.WriteAllText(dbPath,
                "MonoBehaviour:\n  _entries:\n  - key: anim/test\n    asset: {fileID: 21300000, guid: " + guid + ", type: 3}\n");

            var loc = new AssetLocator(sandbox);
            loc.Build();

            var entity = JsonNode.Parse("""
                { "id": "test", "animConfigKey": "anim/test", "stats": null }
                """)!.AsObject();
            var schema = SchemaBuilder.For<EnemyData>();

            var resolved = AssetResolver.Resolve(schema, entity, loc);
            Assert.Single(resolved);
            Assert.Equal("anim/test", resolved[0].AssetKey);
            Assert.Equal("animConfigKey", resolved[0].JsonPath);
            Assert.Equal(pngPath, resolved[0].ImagePath);
            Assert.Equal(128, resolved[0].Width);
            Assert.Equal(32, resolved[0].Height);
            Assert.Equal("enemy-sprite", resolved[0].Pipeline);
        }
        finally { Directory.Delete(sandbox, recursive: true); }
    }

    // ── Round-trip against sibling ozx_base (skips silently) ─────────────

    [Fact]
    public void AssetLocator_resolves_real_ozx_keys_when_sibling_present()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var loc = new AssetLocator(ozx);
        Assert.True(loc.Build());

        // skill/attack_boost@icon is a type=3 PNG and should resolve directly.
        var hit = loc.Resolve("skill/attack_boost@icon");
        Assert.NotNull(hit.ImagePath);
        Assert.EndsWith(".png", hit.ImagePath, StringComparison.OrdinalIgnoreCase);
        _out.WriteLine($"skill/attack_boost@icon -> {hit.ImagePath}");

        // anim/arack_orange is a type=2 .asset; AssetLocator drills for the first PNG.
        var anim = loc.Resolve("anim/arack_orange");
        Assert.NotNull(anim.DirectAssetPath);
        _out.WriteLine($"anim/arack_orange direct={anim.DirectAssetPath} image={anim.ImagePath} hops={anim.Hops}");
        // ImagePath may or may not be found depending on how deep the chain goes;
        // require that we at least know the .asset.
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string WriteFakePng(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"oae-png-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, FakePngBytes(width, height));
        return path;
    }

    private static byte[] FakePngBytes(int width, int height)
    {
        // 24 bytes are enough for PngReader; we don't need a CRC-correct file.
        var bytes = new byte[24];
        // Signature
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes.AsSpan());
        // bytes 8..15: chunk length + "IHDR" — values don't matter for the reader
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        new byte[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), (uint)height);
        return bytes;
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
