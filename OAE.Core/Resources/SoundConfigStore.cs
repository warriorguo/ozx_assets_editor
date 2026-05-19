using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OAE.Core.Resources;

/// <summary>
/// One row in the <c>_sfxEntries:</c> list of <c>SoundConfig.asset</c>.
/// </summary>
public sealed record SfxEntry(
    string SoundId,
    int ClipCount,
    float Volume,
    float PitchMin,
    float PitchMax,
    float Cooldown,
    int Spatial);

/// <summary>
/// Read + delete entries in <c>Assets/Prefabs/System/SoundConfig.asset</c>.
/// Mirrors <see cref="ResourcesDbStore"/>: line-based text surgery so untouched
/// bytes round-trip exactly. v1 supports listing and deleting SFX entries;
/// adding / editing / music entries land in a follow-up.
/// </summary>
public sealed class SoundConfigStore
{
    private static readonly Regex SectionStart = new(@"^[ \t]*_sfxEntries:[ \t]*\r?\n", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex SectionEnd   = new(@"^[ \t]*_musicEntries:", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex EntryStart   = new(@"^[ \t]*- soundId:[ \t]*(?<id>[^\r\n]+?)[ \t]*\r?\n", RegexOptions.Compiled | RegexOptions.Multiline);

    private string _path = string.Empty;
    private string _text = string.Empty;

    public string Path => _path;

    public static string DefaultPathFor(string projectRoot) =>
        System.IO.Path.Combine(projectRoot, "Assets", "Prefabs", "System", "SoundConfig.asset");

    public void Load(string path)
    {
        _path = path;
        _text = File.ReadAllText(path);
    }

    public IReadOnlyList<SfxEntry> List()
    {
        var entries = new List<SfxEntry>();
        foreach (var block in EnumerateSfxBlocks(_text))
            entries.Add(ParseEntry(_text[block.Start..block.End]));
        return entries;
    }

    /// <summary>
    /// Remove the SFX entry with <paramref name="soundId"/>. Returns
    /// <c>false</c> if no such entry exists.
    /// </summary>
    public bool Remove(string soundId)
    {
        foreach (var block in EnumerateSfxBlocks(_text))
        {
            var idMatch = Regex.Match(_text[block.Start..block.End], @"^[ \t]*- soundId:[ \t]*(?<id>[^\r\n]+?)[ \t]*\r?\n", RegexOptions.Multiline);
            if (!idMatch.Success) continue;
            if (idMatch.Groups["id"].Value.Trim() != soundId) continue;
            _text = _text.Remove(block.Start, block.End - block.Start);
            WriteAtomic();
            return true;
        }
        return false;
    }

    private record struct BlockSpan(int Start, int End);

    private static IEnumerable<BlockSpan> EnumerateSfxBlocks(string text)
    {
        var sectionMatch = SectionStart.Match(text);
        if (!sectionMatch.Success) yield break;
        var sectionStart = sectionMatch.Index + sectionMatch.Length;
        var sectionEndMatch = SectionEnd.Match(text, sectionStart);
        var sectionEnd = sectionEndMatch.Success ? sectionEndMatch.Index : text.Length;

        var starts = new List<int>();
        foreach (Match m in EntryStart.Matches(text))
        {
            if (m.Index < sectionStart || m.Index >= sectionEnd) continue;
            starts.Add(m.Index);
        }

        for (var i = 0; i < starts.Count; i++)
        {
            var s = starts[i];
            var e = i + 1 < starts.Count ? starts[i + 1] : sectionEnd;
            yield return new BlockSpan(s, e);
        }
    }

    private static SfxEntry ParseEntry(string block)
    {
        var id = FieldMatch(block, @"^[ \t]*- soundId:[ \t]*([^\r\n]+?)[ \t]*\r?\n") ?? string.Empty;
        var vol = ParseFloat(FieldMatch(block, @"^[ \t]+volume:[ \t]*([^\r\n]+?)[ \t]*\r?\n"));
        var pmin = ParseFloat(FieldMatch(block, @"^[ \t]+pitchMin:[ \t]*([^\r\n]+?)[ \t]*\r?\n"));
        var pmax = ParseFloat(FieldMatch(block, @"^[ \t]+pitchMax:[ \t]*([^\r\n]+?)[ \t]*\r?\n"));
        var cd = ParseFloat(FieldMatch(block, @"^[ \t]+cooldown:[ \t]*([^\r\n]+?)[ \t]*\r?\n"));
        var sp = (int)Math.Round(ParseFloat(FieldMatch(block, @"^[ \t]+spatial:[ \t]*([^\r\n]+?)[ \t]*\r?\n")));
        var clipCount = CountClips(block);
        return new SfxEntry(id.Trim(), clipCount, vol, pmin, pmax, cd, sp);
    }

    private static string? FieldMatch(string block, string pattern)
    {
        var m = Regex.Match(block, pattern, RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static float ParseFloat(string? s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;

    private static int CountClips(string block)
    {
        // clips: []      → 0
        // clips:
        // - {fileID: ..., guid: ..., type: ...}
        // - {...}        → N
        var emptyMatch = Regex.Match(block, @"^[ \t]+clips:[ \t]*\[\][ \t]*\r?\n", RegexOptions.Multiline);
        if (emptyMatch.Success) return 0;
        // Count "- {fileID:" lines inside the clips block. The clips: line is
        // followed by zero or more such lines until the next bare key (e.g.
        // volume:). Cheap approximation: count `- {fileID:` occurrences.
        return Regex.Matches(block, @"^[ \t]+-[ \t]+\{fileID:", RegexOptions.Multiline).Count;
    }

    private void WriteAtomic()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, _text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmp, _path, overwrite: true);
    }
}
