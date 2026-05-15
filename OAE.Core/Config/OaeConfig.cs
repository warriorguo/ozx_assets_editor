using System.Text.Json;
using System.Text.Json.Serialization;

namespace OAE.Core.Config;

/// <summary>
/// Persisted user-level config for OAE. One file per user, lives at
/// <see cref="DefaultPath"/>. The mounted project root and a few app-level
/// preferences are the only things stored here.
/// </summary>
public sealed class OaeConfig
{
    [JsonPropertyName("project_root")]
    public string ProjectRoot { get; set; } = string.Empty;

    [JsonPropertyName("auto_open_last_project")]
    public bool AutoOpenLastProject { get; set; } = true;

    /// <summary>
    /// Optional override for the path to the import-asset Python script used by
    /// OAE-15's drop-zone. <c>null</c> = autodiscover at
    /// <c>~/.claude/skills/import-asset/scripts/import_asset.py</c>.
    /// </summary>
    [JsonPropertyName("import_asset_skill_path")]
    public string? ImportAssetSkillPath { get; set; }

    /// <summary>
    /// ~/.config/oae/config.json on macOS/Linux. Uses XDG-style placement so it
    /// stays out of ~/Library and is easy to inspect by hand.
    /// </summary>
    public static string DefaultPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "oae", "config.json");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Read the config from <paramref name="path"/>, returning defaults when the
    /// file doesn't exist yet so first-run is seamless.
    /// </summary>
    public static OaeConfig Load(string path)
    {
        if (!File.Exists(path)) return new OaeConfig();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<OaeConfig>(json, JsonOpts) ?? new OaeConfig();
    }

    /// <summary>
    /// Save atomically via <c>.tmp</c> + rename so a crash mid-write can't
    /// corrupt the file.
    /// </summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(this, JsonOpts) + "\n";
        File.WriteAllText(tmp, json);
        // File.Move with overwrite=true is atomic on POSIX and best-effort on Windows.
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>
/// View returned to the UI: the persisted config plus computed paths and a
/// fallback flag so the status banner knows whether the project is usable
/// without doing the work itself.
/// </summary>
public sealed record ResolvedConfig(
    OaeConfig Config,
    string ConfigPath,
    string GameDataDir,
    bool ProjectRootExists,
    bool UsesFallback,
    string? FallbackReason)
{
    public static ResolvedConfig Resolve(string configPath, OaeConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.ProjectRoot))
        {
            return new ResolvedConfig(cfg, configPath, string.Empty, false, true, "project_root not set");
        }
        if (!Directory.Exists(cfg.ProjectRoot))
        {
            return new ResolvedConfig(cfg, configPath, string.Empty, false, true, "project_root does not exist");
        }
        var gameData = Path.Combine(cfg.ProjectRoot, "Assets", "StreamingAssets", "GameData");
        if (!Directory.Exists(gameData))
        {
            return new ResolvedConfig(cfg, configPath, gameData, true, true,
                "Assets/StreamingAssets/GameData not found under project_root");
        }
        return new ResolvedConfig(cfg, configPath, gameData, true, false, null);
    }
}
