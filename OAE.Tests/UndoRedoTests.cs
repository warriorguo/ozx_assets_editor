using System.Linq;
using OAE.App.ViewModels;
using OAE.Core.Config;
using OAE.Core.Store;

namespace OAE.Tests;

/// <summary>
/// OAE-54: model-level undo/redo over the canonical entity, unifying edits made
/// through the form (NotifyFormMutated) and the Raw JSON tab (RawJson setter).
/// </summary>
public class UndoRedoTests
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
        public MainWindowViewModel Vm { get; }

        public Sandbox()
        {
            Root = Directory.CreateTempSubdirectory("oae-undo-").FullName;
            var gameData = Path.Combine(Root, "Assets", "StreamingAssets", "GameData");
            Directory.CreateDirectory(Path.Combine(gameData, "enemies"));
            File.WriteAllText(Path.Combine(gameData, "enemies", "test_bug.json"), SampleEnemy);
            File.WriteAllText(Path.Combine(gameData, "enemies", "other_bug.json"),
                SampleEnemy.Replace("test_bug", "other_bug").Replace("Test Bug", "Other Bug"));

            var configPath = Path.Combine(Root, "oae.json");
            var config = new OaeConfig { ProjectRoot = Root };
            Vm = new MainWindowViewModel(new HotSwapStore(new StubStore()), config, configPath);
            Select("test_bug");
        }

        public void Select(string id)
        {
            Vm.SelectedType = Vm.Types.First(t => t.Id == "enemies");
            Vm.SelectedEntity = Vm.Entities.First(e => e.Id == id);
        }

        private static string Name(MainWindowViewModel vm) =>
            vm.CurrentEntity!["displayName"]!.GetValue<string>();

        public string Name() => Name(Vm);

        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }

    [Fact]
    public void Fresh_load_has_no_history()
    {
        using var sb = new Sandbox();
        Assert.False(sb.Vm.CanUndo);
        Assert.False(sb.Vm.CanRedo);
        Assert.False(sb.Vm.IsDirty);
    }

    [Fact]
    public void Raw_edit_then_undo_restores_prior_value_and_clears_dirty()
    {
        using var sb = new Sandbox();
        sb.Vm.RawJson = SampleEnemy.Replace("\"Test Bug\"", "\"Edited\"");
        Assert.Equal("Edited", sb.Name());
        Assert.True(sb.Vm.CanUndo);
        Assert.True(sb.Vm.IsDirty);

        sb.Vm.Undo();

        Assert.Equal("Test Bug", sb.Name());
        Assert.False(sb.Vm.CanUndo);
        Assert.True(sb.Vm.CanRedo);
        // Back at the loaded baseline → not dirty.
        Assert.False(sb.Vm.IsDirty);
    }

    [Fact]
    public void Redo_reapplies_the_undone_edit()
    {
        using var sb = new Sandbox();
        sb.Vm.RawJson = SampleEnemy.Replace("\"Test Bug\"", "\"Edited\"");
        sb.Vm.Undo();
        Assert.Equal("Test Bug", sb.Name());

        sb.Vm.Redo();

        Assert.Equal("Edited", sb.Name());
        Assert.True(sb.Vm.CanUndo);
        Assert.False(sb.Vm.CanRedo);
        Assert.True(sb.Vm.IsDirty);
    }

    [Fact]
    public void Form_mutation_is_undoable_and_stays_in_sync_with_raw()
    {
        using var sb = new Sandbox();
        // Simulate a form edit: mutate the live JsonObject the controls hold, then notify.
        sb.Vm.CurrentEntity!["displayName"] = "Form Edit";
        sb.Vm.NotifyFormMutated();
        Assert.True(sb.Vm.CanUndo);
        Assert.Contains("Form Edit", sb.Vm.RawJson);

        sb.Vm.Undo();

        Assert.Equal("Test Bug", sb.Name());
        Assert.Contains("Test Bug", sb.Vm.RawJson); // Raw tab tracks the undo
    }

    [Fact]
    public void Consecutive_raw_edits_coalesce_into_one_undo_step()
    {
        using var sb = new Sandbox();
        sb.Vm.RawJson = SampleEnemy.Replace("\"Test Bug\"", "\"A\"");
        sb.Vm.RawJson = SampleEnemy.Replace("\"Test Bug\"", "\"AB\"");
        sb.Vm.RawJson = SampleEnemy.Replace("\"Test Bug\"", "\"ABC\"");
        Assert.Equal("ABC", sb.Name());

        // A single undo unwinds the whole typing run back to the baseline.
        sb.Vm.Undo();

        Assert.Equal("Test Bug", sb.Name());
        Assert.False(sb.Vm.CanUndo);
    }

    [Fact]
    public void Form_edit_after_raw_run_opens_a_separate_undo_step()
    {
        using var sb = new Sandbox();
        sb.Vm.RawJson = SampleEnemy.Replace("\"Test Bug\"", "\"Raw\"");
        sb.Vm.CurrentEntity!["displayName"] = "FormAfter";
        sb.Vm.NotifyFormMutated();

        sb.Vm.Undo(); // undoes the form edit only
        Assert.Equal("Raw", sb.Name());
        sb.Vm.Undo(); // undoes the raw edit
        Assert.Equal("Test Bug", sb.Name());
        Assert.False(sb.Vm.CanUndo);
    }

    [Fact]
    public void New_edit_after_undo_discards_the_redo_branch()
    {
        using var sb = new Sandbox();
        sb.Vm.RawJson = SampleEnemy.Replace("\"Test Bug\"", "\"First\"");
        sb.Vm.Undo();
        Assert.True(sb.Vm.CanRedo);

        sb.Vm.RawJson = SampleEnemy.Replace("\"Test Bug\"", "\"Second\"");

        Assert.False(sb.Vm.CanRedo);
        Assert.Equal("Second", sb.Name());
    }

    [Fact]
    public void Switching_entity_resets_history()
    {
        using var sb = new Sandbox();
        sb.Vm.RawJson = SampleEnemy.Replace("\"Test Bug\"", "\"Edited\"");
        Assert.True(sb.Vm.CanUndo);

        sb.Select("other_bug");

        Assert.False(sb.Vm.CanUndo);
        Assert.False(sb.Vm.CanRedo);
        Assert.Equal("Other Bug", sb.Name());
    }

    [Fact]
    public void Save_is_a_history_boundary()
    {
        using var sb = new Sandbox();
        sb.Vm.RawJson = SampleEnemy.Replace("\"Test Bug\"", "\"Saved\"");
        Assert.True(sb.Vm.CanUndo);

        sb.Vm.Save();

        Assert.False(sb.Vm.CanUndo);
        Assert.False(sb.Vm.CanRedo);
        Assert.False(sb.Vm.IsDirty);
    }
}
