using OAE.Core.Config;

namespace OAE.Tests;

public class ConfigTests
{
    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oae-test-{Guid.NewGuid():N}.json");
        var cfg = OaeConfig.Load(path);
        Assert.Equal(string.Empty, cfg.ProjectRoot);
        Assert.True(cfg.AutoOpenLastProject);
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oae-test-{Guid.NewGuid():N}.json");
        try
        {
            var original = new OaeConfig { ProjectRoot = "/tmp/some/path", AutoOpenLastProject = false };
            original.Save(path);
            var loaded = OaeConfig.Load(path);
            Assert.Equal(original.ProjectRoot, loaded.ProjectRoot);
            Assert.Equal(original.AutoOpenLastProject, loaded.AutoOpenLastProject);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Resolve_flags_unset_root_as_fallback()
    {
        var r = ResolvedConfig.Resolve("/tmp/whatever.json", new OaeConfig());
        Assert.True(r.UsesFallback);
        Assert.False(r.ProjectRootExists);
        Assert.Equal("project_root not set", r.FallbackReason);
    }

    [Fact]
    public void Resolve_flags_missing_GameData_dir()
    {
        var tmp = Directory.CreateTempSubdirectory("oae-noGameData-").FullName;
        try
        {
            var r = ResolvedConfig.Resolve("/tmp/whatever.json", new OaeConfig { ProjectRoot = tmp });
            Assert.True(r.UsesFallback);
            Assert.True(r.ProjectRootExists);
            Assert.Contains("GameData", r.FallbackReason ?? "");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void Resolve_succeeds_when_GameData_present()
    {
        var tmp = Directory.CreateTempSubdirectory("oae-ok-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "Assets", "StreamingAssets", "GameData"));
            var r = ResolvedConfig.Resolve("/tmp/whatever.json", new OaeConfig { ProjectRoot = tmp });
            Assert.False(r.UsesFallback);
            Assert.True(r.ProjectRootExists);
            Assert.Null(r.FallbackReason);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
