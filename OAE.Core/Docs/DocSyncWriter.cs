using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OAE.Core.Docs;

/// <summary>One cell that was actually changed by <see cref="DocSyncWriter.SyncEntity"/>.</summary>
public sealed record DocCellChange(string Label, string From, string To);

public enum DocSyncStatus { Updated, Unchanged, NotFound, NoMapping }

/// <summary>
/// Outcome of a sync attempt. <see cref="FilePath"/> is non-null when a doc
/// was located, regardless of whether any edits landed.
/// </summary>
public sealed record DocSyncResult(
    DocSyncStatus Status,
    string? FilePath,
    IReadOnlyList<DocCellChange> Changes);

/// <summary>
/// Propagate entity edits into the matching <c>Documents/&lt;Type&gt;/*.md</c>
/// catalog file by surgically updating only the table cells we know how to
/// map from JSON. Everything else stays byte-equivalent so hand-authored prose
/// is implicitly preserved.
/// </summary>
public sealed class DocSyncWriter
{
    /// <summary>
    /// JSON dotted path -> the label in the leftmost column of the markdown
    /// table whose value we're going to update. Per-type because table labels
    /// are localised (Chinese in ozx_base).
    /// </summary>
    private static readonly Dictionary<string, (string Subdir, IReadOnlyList<(string JsonPath, string Label)> Mapping)> ByType =
        new(StringComparer.Ordinal)
        {
            ["enemies"] = ("Enemy", new (string, string)[]
            {
                ("id",             "ID"),
                ("displayName",    "显示名"),
                ("type",           "类型"),
                ("category",       "分类"),
                ("movementType",   "移动方式"),
                ("attackType",     "攻击方式"),
                ("dropTableId",    "掉落表"),
                ("stats.hp",       "HP"),
                ("stats.moveSpeed","移动速度"),
                ("stats.attack",   "攻击力"),
                ("stats.defense",  "防御力"),
                ("stats.attackRange","攻击范围"),
            }),
        };

    /// <summary>
    /// Find the doc whose 基本信息 table identifies <paramref name="entityId"/>
    /// (line `| ID | <entityId> |`), then update each mapped cell whose JSON
    /// value differs.
    /// </summary>
    public DocSyncResult SyncEntity(string typeId, string entityId, JsonObject body, string docsRoot)
    {
        if (!ByType.TryGetValue(typeId, out var spec))
            return new DocSyncResult(DocSyncStatus.NoMapping, null, Array.Empty<DocCellChange>());

        var typeDir = Path.Combine(docsRoot, spec.Subdir);
        if (!Directory.Exists(typeDir))
            return new DocSyncResult(DocSyncStatus.NotFound, null, Array.Empty<DocCellChange>());

        var docPath = FindDocForId(typeDir, entityId);
        if (docPath is null)
            return new DocSyncResult(DocSyncStatus.NotFound, null, Array.Empty<DocCellChange>());

        var text = File.ReadAllText(docPath);
        var changes = new List<DocCellChange>();

        foreach (var (jsonPath, label) in spec.Mapping)
        {
            var jsonValue = ResolveJsonValue(body, jsonPath);
            if (jsonValue is null) continue; // field absent — don't try to write
            var (replaced, oldValue) = TryReplaceCell(text, label, jsonValue);
            if (replaced is null) continue; // label not present in this doc — skip
            if (CellsEquivalent(oldValue, jsonValue)) continue; // no semantic change
            text = replaced;
            changes.Add(new DocCellChange(label, oldValue, jsonValue));
        }

        if (changes.Count == 0)
            return new DocSyncResult(DocSyncStatus.Unchanged, docPath, Array.Empty<DocCellChange>());

        WriteAtomic(docPath, text);
        return new DocSyncResult(DocSyncStatus.Updated, docPath, changes);
    }

    private static string? FindDocForId(string typeDir, string entityId)
    {
        // Look for `| ID | <entityId> |` line. Cheap pre-filter by basename
        // containing the id wouldn't catch some renames so we just scan.
        var marker = new Regex(@"^\|\s*ID\s*\|\s*" + Regex.Escape(entityId) + @"\s*\|", RegexOptions.Multiline);
        foreach (var path in Directory.EnumerateFiles(typeDir, "*.md", SearchOption.TopDirectoryOnly))
        {
            string text;
            try { text = File.ReadAllText(path); } catch { continue; }
            if (marker.IsMatch(text)) return path;
        }
        return null;
    }

    /// <summary>
    /// Cell value equality with two relaxations: trim whitespace, and treat
    /// numeric values as equal when they parse to the same double. That stops
    /// "1.0" in the doc and the System.Text.Json-formatted "1" from the JSON
    /// number from triggering a no-op rewrite (which would still flip "1.0"
    /// to "1" on disk and bloat the diff).
    /// </summary>
    private static bool CellsEquivalent(string a, string b)
    {
        var ta = a.Trim();
        var tb = b.Trim();
        if (string.Equals(ta, tb, StringComparison.Ordinal)) return true;
        if (double.TryParse(ta, NumberStyles.Float, CultureInfo.InvariantCulture, out var da)
            && double.TryParse(tb, NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
        {
            return da == db;
        }
        return false;
    }

    private static (string? NewText, string OldValue) TryReplaceCell(string text, string label, string newValue)
    {
        // Match `| <label> | <oldValue> |` with whitespace allowed.
        var pattern = new Regex(
            @"^(\|\s*" + Regex.Escape(label) + @"\s*\|\s*)([^|\r\n]*?)(\s*\|.*)$",
            RegexOptions.Multiline);
        var match = pattern.Match(text);
        if (!match.Success) return (null, string.Empty);
        var oldValue = match.Groups[2].Value.TrimEnd();
        var replacement = match.Groups[1].Value + newValue + match.Groups[3].Value;
        var newText = text[..match.Index] + replacement + text[(match.Index + match.Length)..];
        return (newText, oldValue);
    }

    private static string? ResolveJsonValue(JsonObject root, string dottedPath)
    {
        JsonNode? node = root;
        foreach (var segment in dottedPath.Split('.'))
        {
            if (node is not JsonObject obj) return null;
            node = obj[segment];
        }
        return FormatJsonValue(node);
    }

    private static string? FormatJsonValue(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonValue v)
        {
            // Try strings first (most common in our mapping).
            if (v.TryGetValue<string>(out var s)) return s;
            // Then floats — keep the canonical form (avoid culture-specific commas).
            if (v.TryGetValue<double>(out var d))
                return d.ToString(CultureInfo.InvariantCulture);
            return v.ToJsonString();
        }
        return null;
    }

    private static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmp, path, overwrite: true);
    }
}
