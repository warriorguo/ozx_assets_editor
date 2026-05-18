using System.Text.Json.Nodes;
using OAE.Core.Store;
using OAE.Core.Templates;

namespace OAE.Tests;

public class CreateFromTemplateTests
{
    [Fact]
    public void BuildBodyForNewEntity_overrides_id_and_optional_displayName()
    {
        var t = TemplateLoader.Get("enemies", "kamikaze");
        Assert.NotNull(t);
        var json = TemplateLoader.BuildBodyForNewEntity(t!, "test_bug", "Test Bug");
        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("test_bug", obj["id"]!.GetValue<string>());
        Assert.Equal("Test Bug", obj["displayName"]!.GetValue<string>());
        Assert.Equal("EnemyData", obj["dataType"]!.GetValue<string>());
    }

    [Fact]
    public void BuildBodyForNewEntity_skips_displayName_when_not_supplied()
    {
        var t = TemplateLoader.Get("enemies", "kamikaze");
        Assert.NotNull(t);
        var json = TemplateLoader.BuildBodyForNewEntity(t!, "x", null);
        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(string.Empty, obj["displayName"]!.GetValue<string>()); // template default
    }

    [Fact]
    public void Create_via_FsStore_then_Get_round_trips_template_body()
    {
        var sandbox = Directory.CreateTempSubdirectory("oae-newentity-").FullName;
        try
        {
            var gameData = Path.Combine(sandbox, "Assets", "StreamingAssets", "GameData");
            Directory.CreateDirectory(Path.Combine(gameData, "enemies"));
            var store = new FsStore(gameData);

            var t = TemplateLoader.Get("enemies", "ranged")!;
            var json = TemplateLoader.BuildBodyForNewEntity(t, "new_ranged", "Ranged Bug") + "\n";
            store.Create("enemies", "new_ranged", json);

            var raw = store.Get("enemies", "new_ranged");
            Assert.Contains("\"id\": \"new_ranged\"", raw);
            Assert.Contains("\"attackType\": \"ranged\"", raw);
            Assert.EndsWith("\n", raw);
        }
        finally { Directory.Delete(sandbox, recursive: true); }
    }

    [Fact]
    public void Duplicate_create_throws_InvalidOperation()
    {
        var sandbox = Directory.CreateTempSubdirectory("oae-dup-").FullName;
        try
        {
            var gameData = Path.Combine(sandbox, "Assets", "StreamingAssets", "GameData");
            Directory.CreateDirectory(Path.Combine(gameData, "enemies"));
            var store = new FsStore(gameData);
            var t = TemplateLoader.Get("enemies", "kamikaze")!;
            store.Create("enemies", "dup", TemplateLoader.BuildBodyForNewEntity(t, "dup", null) + "\n");
            Assert.Throws<InvalidOperationException>(() =>
                store.Create("enemies", "dup", TemplateLoader.BuildBodyForNewEntity(t, "dup", null) + "\n"));
        }
        finally { Directory.Delete(sandbox, recursive: true); }
    }
}
