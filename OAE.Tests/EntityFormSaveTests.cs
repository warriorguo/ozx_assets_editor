using System.Text.Json;
using System.Text.Json.Nodes;
using OAE.Core.Store;

namespace OAE.Tests;

/// <summary>
/// Non-UI tests covering the contract OAE-5's form save flow depends on:
/// JsonObject mutation preserves insertion key order and serialises with
/// 2-space indent, then a write through FsStore round-trips.
/// </summary>
public class EntityFormSaveTests
{
    private const string SampleEnemy = """
{
  "dataType": "EnemyData",
  "id": "test_bug",
  "displayName": "Test Bug",
  "type": "normal",
  "stats": {
    "hp": 35,
    "moveSpeed": 1.0,
    "attack": 10,
    "defense": 0,
    "attackRange": 3.0
  },
  "size": 0.15,
  "category": "bug"
}
""";

    private static readonly JsonSerializerOptions SaveOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    [Fact]
    public void Mutating_nested_field_preserves_other_keys_and_order()
    {
        var obj = JsonNode.Parse(SampleEnemy)!.AsObject();

        // Simulate the form-builder path: stats is a nested object; hp moves from 35 -> 40.
        var stats = obj["stats"]!.AsObject();
        stats["hp"] = JsonValue.Create(40);

        var result = obj.ToJsonString(SaveOpts);

        // Mutated value present.
        Assert.Contains("\"hp\": 40", result);
        // Untouched siblings still present, in the same order.
        var hpIdx       = result.IndexOf("\"hp\"");
        var moveIdx     = result.IndexOf("\"moveSpeed\"");
        var attackIdx   = result.IndexOf("\"attack\"");
        var defenseIdx  = result.IndexOf("\"defense\"");
        Assert.True(hpIdx < moveIdx && moveIdx < attackIdx && attackIdx < defenseIdx,
            "key order within stats was not preserved");
        // Top-level order also preserved (dataType -> id -> displayName -> type -> stats -> size -> category).
        Assert.True(result.IndexOf("\"dataType\"") < result.IndexOf("\"id\""));
        Assert.True(result.IndexOf("\"id\"") < result.IndexOf("\"displayName\""));
        Assert.True(result.IndexOf("\"stats\"") < result.IndexOf("\"size\""));
    }

    [Fact]
    public void Save_round_trip_through_FsStore_lands_only_intended_change()
    {
        var sandbox = Directory.CreateTempSubdirectory("oae-form-save-").FullName;
        try
        {
            var gameData = Path.Combine(sandbox, "Assets", "StreamingAssets", "GameData");
            Directory.CreateDirectory(Path.Combine(gameData, "enemies"));
            var path = Path.Combine(gameData, "enemies", "test_bug.json");
            File.WriteAllText(path, SampleEnemy);

            var store = new FsStore(gameData);

            // Form-builder behaviour, in miniature: load, mutate, save.
            var raw = store.Get("enemies", "test_bug");
            var obj = JsonNode.Parse(raw)!.AsObject();
            obj["stats"]!.AsObject()["hp"] = JsonValue.Create(40);
            var savedJson = obj.ToJsonString(SaveOpts) + "\n";
            store.Update("enemies", "test_bug", savedJson);

            var rereadRaw = store.Get("enemies", "test_bug");
            var rereadObj = JsonNode.Parse(rereadRaw)!.AsObject();

            Assert.Equal(40, rereadObj["stats"]!.AsObject()["hp"]!.GetValue<int>());
            Assert.Equal("test_bug", rereadObj["id"]!.GetValue<string>());
            Assert.Equal("bug", rereadObj["category"]!.GetValue<string>());
        }
        finally { try { Directory.Delete(sandbox, recursive: true); } catch { } }
    }
}
