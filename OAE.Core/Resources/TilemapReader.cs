using System.Text.Json;
using System.Text.Json.Serialization;

namespace OAE.Core.Resources;

/// <summary>
/// Reads <c>Assets/StreamingAssets/TilemapData/&lt;theme&gt;/*.json</c> files
/// authored for the OZX room-template system (sister project ORT). Surface
/// is intentionally minimal — enough for OAE to browse, preview, and look
/// up reverse references. Editing lives in ORT.
/// </summary>
public sealed class TilemapDocument
{
    public string? StageType { get; set; }
    public string? RoomShape { get; set; }
    public string? RoomCategory { get; set; }
    public int OpenDoors { get; set; }
    public DoorMap Doors { get; set; } = new();
    public TilemapMeta Meta { get; set; } = new();

    // 12×20 grids of int tile codes. Cell value > 0 means "this layer covers
    // this cell" — semantics vary per layer but a non-zero is enough for the
    // browse preview to differentiate layers visually.
    public int[][]? Ground { get; set; }
    public int[][]? SoftEdge { get; set; }
    public int[][]? Bridge { get; set; }
    public int[][]? Static { get; set; }
    public int[][]? Chaser { get; set; }
    public int[][]? Zoner { get; set; }
    public int[][]? Dps { get; set; }
    public int[][]? MobAir { get; set; }
    public int[][]? MainPath { get; set; }
    public int[][]? Rail { get; set; }
    public int[][]? Pipeline { get; set; }

    public int Width => Meta.Width > 0 ? Meta.Width : (Ground?[0]?.Length ?? 0);
    public int Height => Meta.Height > 0 ? Meta.Height : (Ground?.Length ?? 0);
}

public sealed class DoorMap
{
    public int Left { get; set; }
    public int Right { get; set; }
    public int Top { get; set; }
    public int Bottom { get; set; }
}

public sealed class TilemapMeta
{
    public string? Name { get; set; }
    public int Version { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public static class TilemapReader
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        // The TilemapData JSON uses lowercase keys ("ground", "softEdge", …).
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static TilemapDocument Read(string path)
    {
        var json = File.ReadAllText(path);
        return Parse(json);
    }

    public static TilemapDocument Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<TilemapDocument>(json, Opts);
        return doc ?? new TilemapDocument();
    }
}
