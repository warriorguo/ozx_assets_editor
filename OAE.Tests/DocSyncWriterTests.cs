using System.Text.Json.Nodes;
using OAE.Core.Docs;
using Xunit.Abstractions;

namespace OAE.Tests;

public class DocSyncWriterTests
{
    private readonly ITestOutputHelper _out;
    public DocSyncWriterTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Sync_arack_orange_updates_HP_cell_only()
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine("ozx_base sibling not found — skipped."); return; }

        var src = Path.Combine(ozx, "Documents", "Enemy", "ENM_Arack_Orange.md");
        if (!File.Exists(src)) { _out.WriteLine("ENM_Arack_Orange.md not found — skipped."); return; }

        var sandboxDir = Directory.CreateTempSubdirectory("oae-docsync-").FullName;
        try
        {
            var docsRoot = Path.Combine(sandboxDir, "Documents");
            var enemyDir = Path.Combine(docsRoot, "Enemy");
            Directory.CreateDirectory(enemyDir);
            var sandboxDoc = Path.Combine(enemyDir, "ENM_Arack_Orange.md");
            File.Copy(src, sandboxDoc);
            var originalBytes = File.ReadAllBytes(sandboxDoc);

            // Build a minimal JsonObject mirroring arack_orange with hp=40
            // (was 35). Other mapped fields unchanged.
            var body = JsonNode.Parse("""
                {
                  "id": "arack_orange",
                  "displayName": "Arack Orange",
                  "type": "normal",
                  "category": "bug",
                  "movementType": "ground",
                  "attackType": "kamikaze",
                  "dropTableId": "loot_common",
                  "stats": {
                    "hp": 40,
                    "moveSpeed": 1.0,
                    "attack": 10,
                    "defense": 0,
                    "attackRange": 3.0
                  }
                }
            """)!.AsObject();

            var writer = new DocSyncWriter();
            var result = writer.SyncEntity("enemies", "arack_orange", body, docsRoot);

            Assert.Equal(DocSyncStatus.Updated, result.Status);
            Assert.Equal(sandboxDoc, result.FilePath);
            Assert.Contains(result.Changes, c => c.Label == "HP" && c.From == "35" && c.To == "40");

            var newText = File.ReadAllText(sandboxDoc);
            Assert.Contains("| HP | 40 |", newText);
            Assert.DoesNotContain("| HP | 35 |", newText);

            // Confirm nothing else moved — diff against original should be a
            // single 1-character change in the HP line. Cheaper proxy: identical
            // length ±1 char (35→40 is same length, so exactly equal length).
            var newBytes = File.ReadAllBytes(sandboxDoc);
            Assert.Equal(originalBytes.Length, newBytes.Length);

            // And re-running with the same body is a no-op.
            var second = writer.SyncEntity("enemies", "arack_orange", body, docsRoot);
            Assert.Equal(DocSyncStatus.Unchanged, second.Status);
        }
        finally { Directory.Delete(sandboxDir, recursive: true); }
    }

    [Fact]
    public void Sync_returns_NotFound_when_no_doc_matches_id()
    {
        var sandboxDir = Directory.CreateTempSubdirectory("oae-docsync-nf-").FullName;
        try
        {
            var docsRoot = Path.Combine(sandboxDir, "Documents");
            Directory.CreateDirectory(Path.Combine(docsRoot, "Enemy"));
            var body = JsonNode.Parse("""{"id":"ghost","stats":{"hp":1}}""")!.AsObject();
            var result = new DocSyncWriter().SyncEntity("enemies", "ghost", body, docsRoot);
            Assert.Equal(DocSyncStatus.NotFound, result.Status);
            Assert.Null(result.FilePath);
        }
        finally { Directory.Delete(sandboxDir, recursive: true); }
    }

    [Fact]
    public void Sync_returns_NoMapping_for_unknown_type()
    {
        var sandboxDir = Directory.CreateTempSubdirectory("oae-docsync-nm-").FullName;
        try
        {
            var docsRoot = Path.Combine(sandboxDir, "Documents");
            var body = JsonNode.Parse("""{"id":"x"}""")!.AsObject();
            var result = new DocSyncWriter().SyncEntity("not_a_real_type", "x", body, docsRoot);
            Assert.Equal(DocSyncStatus.NoMapping, result.Status);
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
