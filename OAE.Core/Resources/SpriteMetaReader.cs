using System.Globalization;
using System.Text.RegularExpressions;

namespace OAE.Core.Resources;

/// <summary>
/// Salient fields from a Unity <c>*.png.meta</c> / <c>*.jpg.meta</c>:
/// the sprite mode (Single vs Multiple), the sprite count when Multiple,
/// and the PPU at import time.
/// </summary>
public sealed record SpriteMeta(bool IsMultiple, int SpriteCount, int PixelsPerUnit);

/// <summary>
/// Lightweight extractor for the sprite-importer fields OAE cares about.
/// Regex-only — no general YAML parser dependency. Returns <c>null</c> when
/// the file isn't a Unity texture meta we can interpret.
/// </summary>
public static class SpriteMetaReader
{
    private static readonly Regex SpriteModeLine =
        new(@"^[ \t]*spriteMode:[ \t]*(\d+)[ \t]*\r?$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex PpuLine =
        new(@"^[ \t]*spritePixelsToUnits:[ \t]*(\d+(?:\.\d+)?)[ \t]*\r?$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex SpriteSheetBlock =
        new(@"^[ \t]*spriteSheet:\s*\r?\n", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex SpriteNameLine =
        new(@"^[ \t]+name:\s*\S", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Parse the .meta at <paramref name="metaPath"/>. Returns <c>null</c> if
    /// the file is missing or doesn't carry a TextureImporter sprite section.
    /// </summary>
    public static SpriteMeta? Read(string metaPath)
    {
        if (!File.Exists(metaPath)) return null;
        var text = File.ReadAllText(metaPath);

        var modeMatch = SpriteModeLine.Match(text);
        if (!modeMatch.Success) return null;
        var spriteMode = int.Parse(modeMatch.Groups[1].Value);

        var ppuMatch = PpuLine.Match(text);
        var ppu = ppuMatch.Success
            ? (int)Math.Round(float.Parse(ppuMatch.Groups[1].Value, CultureInfo.InvariantCulture))
            : 100;

        var isMultiple = spriteMode == 2;
        var spriteCount = 0;
        if (isMultiple)
        {
            // Count `name:` lines INSIDE the spriteSheet: block. Each entry in
            // the sprites: list has exactly one name. We bound the count to
            // the substring starting at spriteSheet: to avoid catching
            // platform-setting blocks earlier in the file.
            var sheetMatch = SpriteSheetBlock.Match(text);
            if (sheetMatch.Success)
            {
                var tail = text[sheetMatch.Index..];
                spriteCount = SpriteNameLine.Matches(tail).Count;
            }
        }

        return new SpriteMeta(isMultiple, spriteCount, ppu);
    }
}
