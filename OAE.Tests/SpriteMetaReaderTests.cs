using OAE.Core.Resources;
using Xunit.Abstractions;

namespace OAE.Tests;

public class SpriteMetaReaderTests
{
    private readonly ITestOutputHelper _out;
    public SpriteMetaReaderTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Returns_null_for_missing_file()
    {
        Assert.Null(SpriteMetaReader.Read("/tmp/oae-no-such.meta"));
    }

    [Fact]
    public void Reads_multiple_sliced_sheet_against_sibling_ozx_base()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var meta = SpriteMetaReader.Read(
            Path.Combine(ozx, "Assets", "Images", "Characters", "Arack", "Arack_OrangeWalk.png.meta"));
        Assert.NotNull(meta);
        Assert.True(meta!.IsMultiple);
        Assert.True(meta.SpriteCount > 0, "expected > 0 sliced sprites on Arack_OrangeWalk");
        Assert.True(meta.PixelsPerUnit > 0);
        _out.WriteLine($"sprites: {meta.SpriteCount}, ppu: {meta.PixelsPerUnit}");
    }

    [Fact]
    public void Reads_synthetic_single_mode()
    {
        var meta = WriteTempMeta("""
fileFormatVersion: 2
guid: 0123456789abcdef0123456789abcdef
TextureImporter:
  spriteMode: 1
  spritePixelsToUnits: 100
""");
        try
        {
            var read = SpriteMetaReader.Read(meta);
            Assert.NotNull(read);
            Assert.False(read!.IsMultiple);
            Assert.Equal(0, read.SpriteCount);
            Assert.Equal(100, read.PixelsPerUnit);
        }
        finally { File.Delete(meta); }
    }

    [Fact]
    public void Returns_null_when_not_a_texture_meta()
    {
        var meta = WriteTempMeta("""
fileFormatVersion: 2
guid: 0123456789abcdef0123456789abcdef
NativeFormatImporter:
  externalObjects: {}
""");
        try
        {
            Assert.Null(SpriteMetaReader.Read(meta));
        }
        finally { File.Delete(meta); }
    }

    private static string WriteTempMeta(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"oae-spritemeta-{Guid.NewGuid():N}.meta");
        File.WriteAllText(path, body);
        return path;
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
