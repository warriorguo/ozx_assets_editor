using System.Linq;
using System.Text.Json.Nodes;
using OAE.App.ViewModels;
using OAE.Core.Config;
using OAE.Core.Store;

namespace OAE.Tests;

/// <summary>
/// OAE-33: the Raw JSON tab is bidirectional. Cover the VM contract:
/// valid edits replace CurrentEntity + mark dirty + fire EntityFormChanged;
/// invalid edits populate RawJsonError, leave CurrentEntity untouched, and
/// drop CanSave; form-driven RawJson refreshes do NOT re-parse-and-replace.
/// </summary>
public class RawJsonEditableTests
{
    private const string SampleEnemy = """
{
  "dataType": "EnemyData",
  "id": "test_bug",
  "displayName": "Test Bug",
  "stats": {
    "hp": 35,
    "moveSpeed": 1.0,
    "attack": 10,
    "defense": 0,
    "attackRange": 3.0
  },
  "size": 0.15
}
""";

    private sealed class Sandbox : IDisposable
    {
        public string Root { get; }
        public string ConfigPath { get; }
        public OaeConfig Config { get; }
        public MainWindowViewModel Vm { get; }

        public Sandbox()
        {
            Root = Directory.CreateTempSubdirectory("oae-rawjson-").FullName;
            var gameData = Path.Combine(Root, "Assets", "StreamingAssets", "GameData");
            Directory.CreateDirectory(Path.Combine(gameData, "enemies"));
            File.WriteAllText(Path.Combine(gameData, "enemies", "test_bug.json"), SampleEnemy);

            ConfigPath = Path.Combine(Root, "oae.json");
            Config = new OaeConfig { ProjectRoot = Root };
            Vm = new MainWindowViewModel(new HotSwapStore(new StubStore()), Config, ConfigPath);
            // The constructor walks the resolved config and swaps in an FsStore
            // when ProjectRoot is valid. Pick our seeded entity.
            var enemies = Vm.Types.FirstOrDefault(t => t.Id == "enemies");
            Assert.NotNull(enemies);
            Vm.SelectedType = enemies;
            var test = Vm.Entities.FirstOrDefault(e => e.Id == "test_bug");
            Assert.NotNull(test);
            Vm.SelectedEntity = test;
            Assert.NotNull(Vm.CurrentEntity);
            Assert.NotNull(Vm.CurrentSchema);
        }

        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }

    [Fact]
    public void Valid_edit_swaps_CurrentEntity_marks_dirty_and_fires_change()
    {
        using var sb = new Sandbox();
        var before = sb.Vm.CurrentEntity;
        var events = 0;
        sb.Vm.EntityFormChanged += () => events++;

        // Bump displayName via the JSON tab.
        var edited = SampleEnemy.Replace("\"Test Bug\"", "\"Edited Bug\"");
        sb.Vm.RawJson = edited;

        Assert.NotSame(before, sb.Vm.CurrentEntity);
        Assert.Equal("Edited Bug", sb.Vm.CurrentEntity!["displayName"]!.GetValue<string>());
        Assert.True(sb.Vm.IsDirty);
        Assert.Empty(sb.Vm.RawJsonError);
        Assert.True(sb.Vm.CanSave);
        Assert.Equal(1, events);
    }

    [Fact]
    public void Invalid_edit_records_error_and_blocks_save()
    {
        using var sb = new Sandbox();
        var before = sb.Vm.CurrentEntity;
        var events = 0;
        sb.Vm.EntityFormChanged += () => events++;

        sb.Vm.RawJson = "{ this is not valid json";

        Assert.Same(before, sb.Vm.CurrentEntity);
        Assert.False(string.IsNullOrEmpty(sb.Vm.RawJsonError));
        Assert.False(sb.Vm.CanSave);
        Assert.Equal(0, events);
    }

    [Fact]
    public void Non_object_json_reports_error()
    {
        using var sb = new Sandbox();
        var before = sb.Vm.CurrentEntity;

        sb.Vm.RawJson = "[1, 2, 3]";

        Assert.Same(before, sb.Vm.CurrentEntity);
        Assert.Contains("object", sb.Vm.RawJsonError, StringComparison.OrdinalIgnoreCase);
        Assert.False(sb.Vm.CanSave);
    }

    [Fact]
    public void Recovering_from_invalid_back_to_valid_clears_error_and_reenables_save()
    {
        using var sb = new Sandbox();

        sb.Vm.RawJson = "{ bad";
        Assert.False(string.IsNullOrEmpty(sb.Vm.RawJsonError));

        sb.Vm.RawJson = SampleEnemy.Replace("\"Test Bug\"", "\"Recovered\"");

        Assert.Empty(sb.Vm.RawJsonError);
        Assert.True(sb.Vm.IsDirty);
        Assert.True(sb.Vm.CanSave);
        Assert.Equal("Recovered", sb.Vm.CurrentEntity!["displayName"]!.GetValue<string>());
    }

    [Fact]
    public void Form_driven_refresh_via_NotifyFormMutated_does_not_replace_CurrentEntity()
    {
        using var sb = new Sandbox();
        // Simulate a form mutation: change a value on the live JsonObject the
        // controls hold a reference to.
        sb.Vm.CurrentEntity!["displayName"] = "Form Edit";
        var before = sb.Vm.CurrentEntity; // same reference we just mutated

        sb.Vm.NotifyFormMutated();

        // The VM re-serialises into RawJson, but the OnRawJsonChanged guard
        // must prevent that from parsing-and-swapping CurrentEntity (which
        // would invalidate every control binding still holding `before`).
        Assert.Same(before, sb.Vm.CurrentEntity);
        Assert.True(sb.Vm.IsDirty);
        Assert.Contains("Form Edit", sb.Vm.RawJson);
    }

    [Fact]
    public void Revert_clears_error_and_dirty_after_invalid_edit()
    {
        using var sb = new Sandbox();
        sb.Vm.RawJson = "{ bad";
        Assert.False(string.IsNullOrEmpty(sb.Vm.RawJsonError));

        sb.Vm.Revert();

        Assert.Empty(sb.Vm.RawJsonError);
        Assert.False(sb.Vm.IsDirty);
        Assert.NotNull(sb.Vm.CurrentEntity);
        Assert.Equal("Test Bug", sb.Vm.CurrentEntity!["displayName"]!.GetValue<string>());
    }
}
