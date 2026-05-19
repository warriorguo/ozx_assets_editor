using System.Text;
using System.Text.RegularExpressions;

namespace OAE.Core.Resources;

/// <summary>
/// One row in <c>ResourcesDB.asset</c>'s <c>_entries:</c> list, in the shape
/// the on-disk YAML uses: a human key plus an inline-mapping asset reference
/// (<c>fileID</c>, <c>guid</c>, <c>type</c>).
/// </summary>
public sealed record ResourcesDbEntry(string Key, string Guid, long FileId, int Type);

/// <summary>
/// Read + mutate <c>Assets/Prefabs/System/ResourcesDB.asset</c>.
/// </summary>
/// <remarks>
/// Edits are text-level, not YAML-AST. The file is Unity-managed and
/// sensitive to whitespace / inline-mapping style — round-tripping through a
/// general YAML library would reformat the un-touched bytes and produce noisy
/// `git diff`s (and potentially break Unity). Instead we locate the exact
/// 2-line block for an existing entry and splice; insertions go in
/// alphabetical key order to match ozx_base's existing convention.
/// </remarks>
public sealed class ResourcesDbStore
{
    // One entry is two lines:
    //   - key: <K>
    //     asset: {fileID: <F>, guid: <G>, type: <T>}
    // The leading indents are part of the match so removals don't leave stray
    // whitespace, and so we can detect the file's line ending policy.
    private static readonly Regex EntryBlock = new(
        @"^(?<indent>[ \t]+)- key:\s*(?<key>[^\r\n]+?)\s*(?<eol>\r?\n)" +
        @"(?<indent2>[ \t]+)asset:\s*\{\s*fileID:\s*(?<fid>-?\d+)\s*,\s*guid:\s*(?<guid>[a-f0-9]{32})\s*,\s*type:\s*(?<type>-?\d+)\s*\}\s*\r?\n",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private string _path = string.Empty;
    private string _text = string.Empty;

    public string Path => _path;

    public static string DefaultPathFor(string projectRoot) =>
        ResourcesDbReader.DefaultPathFor(projectRoot);

    /// <summary>
    /// Read the file. Throws if it doesn't exist — the caller decides whether
    /// to create one (rare; ResourcesDB is normally Unity-generated).
    /// </summary>
    public void Load(string path)
    {
        _path = path;
        _text = File.ReadAllText(path);
    }

    /// <summary>
    /// Every entry in document order.
    /// </summary>
    public IReadOnlyList<ResourcesDbEntry> List()
    {
        var entries = new List<ResourcesDbEntry>();
        foreach (Match m in EntryBlock.Matches(_text))
        {
            entries.Add(new ResourcesDbEntry(
                Key: m.Groups["key"].Value.Trim(),
                Guid: m.Groups["guid"].Value.ToLowerInvariant(),
                FileId: long.Parse(m.Groups["fid"].Value),
                Type: int.Parse(m.Groups["type"].Value)));
        }
        return entries;
    }

    public ResourcesDbEntry? Get(string key)
    {
        foreach (var e in List()) if (e.Key == key) return e;
        return null;
    }

    /// <summary>Add a new entry. Throws if <paramref name="key"/> already exists.</summary>
    public void Add(string key, string guid, long fileId, int type)
    {
        if (Get(key) is not null)
            throw new InvalidOperationException($"key already exists: {key}");

        var (indent, indent2, eol) = DetectFormatting();
        var newBlock = $"{indent}- key: {key}{eol}{indent2}asset: {{fileID: {fileId}, guid: {guid.ToLowerInvariant()}, type: {type}}}{eol}";

        // Insert before the first existing entry whose key sorts after the new
        // one, so the on-disk order stays consistent with ozx_base's convention
        // (alphabetical-ish — Unity doesn't enforce, but we don't want to
        // scramble what's there).
        var matches = EntryBlock.Matches(_text);
        var insertAt = -1;
        foreach (Match m in matches)
        {
            var existingKey = m.Groups["key"].Value.Trim();
            if (string.CompareOrdinal(existingKey, key) > 0) { insertAt = m.Index; break; }
        }
        if (insertAt < 0)
        {
            // Insert after the last existing entry; if there are none, after the
            // `_entries:` line.
            if (matches.Count > 0)
                insertAt = matches[^1].Index + matches[^1].Length;
            else
            {
                var anchor = Regex.Match(_text, @"^[ \t]*_entries:\s*\r?\n", RegexOptions.Multiline);
                if (!anchor.Success)
                    throw new InvalidOperationException("could not locate _entries: anchor in ResourcesDB.asset");
                insertAt = anchor.Index + anchor.Length;
            }
        }
        _text = _text.Insert(insertAt, newBlock);
        WriteAtomic();
    }

    /// <summary>Replace an existing entry's metadata. Throws if missing.</summary>
    public void Update(string key, string guid, long fileId, int type)
    {
        var (indent, indent2, eol) = DetectFormatting();
        var replacement = $"{indent}- key: {key}{eol}{indent2}asset: {{fileID: {fileId}, guid: {guid.ToLowerInvariant()}, type: {type}}}{eol}";

        var matched = false;
        _text = EntryBlock.Replace(_text, m =>
        {
            if (m.Groups["key"].Value.Trim() != key) return m.Value;
            matched = true;
            return replacement;
        });
        if (!matched) throw new InvalidOperationException($"key not found: {key}");
        WriteAtomic();
    }

    /// <summary>Remove an entry. No-op if missing.</summary>
    public bool Remove(string key)
    {
        var found = false;
        _text = EntryBlock.Replace(_text, m =>
        {
            if (m.Groups["key"].Value.Trim() != key) return m.Value;
            found = true;
            return string.Empty;
        });
        if (found) WriteAtomic();
        return found;
    }

    private (string indent, string indent2, string eol) DetectFormatting()
    {
        var m = EntryBlock.Match(_text);
        if (m.Success)
            return (m.Groups["indent"].Value, m.Groups["indent2"].Value, m.Groups["eol"].Value);
        // Fallback for a freshly-created empty-entries DB. Unity convention is
        // 2-space indent and \n line endings on macOS.
        return ("  ", "    ", "\n");
    }

    private void WriteAtomic()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, _text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmp, _path, overwrite: true);
    }
}
