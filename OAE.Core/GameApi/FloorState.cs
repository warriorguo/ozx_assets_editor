namespace OAE.Core.GameApi;

/// <summary>
/// Top-level response of <c>GET /api/state/floor</c> on the OZX runtime API
/// (see <c>ozx_base/Assets/Scripts/Game.API/README.md</c>). Describes the
/// minimap structure of the floor the player is currently on: rooms with
/// grid coordinates, doors, type, and per-room enemy/loot previews.
/// </summary>
public sealed class FloorState
{
    public int FloorIndex { get; set; }
    public string? ThemeId { get; set; }
    public string? StartRoomId { get; set; }
    public string? BossRoomId { get; set; }
    public string? CurrentRoomId { get; set; }
    public List<FloorRoomState> Rooms { get; set; } = new();
}

public sealed class FloorRoomState
{
    public string? RoomId { get; set; }
    public string? StageType { get; set; }
    public string? Category { get; set; }
    public string? Shape { get; set; }
    public string? BossId { get; set; }
    public bool Cleared { get; set; }
    public bool Visited { get; set; }
    public bool HasTeleportSpot { get; set; }
    public bool HasLayout { get; set; }
    public int GridX { get; set; }
    public int GridY { get; set; }
    public int Cols { get; set; }
    public int Rows { get; set; }
    public string? SpawnPlanId { get; set; }
    public string? LootPlanId { get; set; }
    // OZX-386: cave/basement rooms are anchored to a parent room (the one you
    // enter them from) so they get a grid position even though they're not on
    // the door graph. Sub-rooms sit one cell down-and-right of the parent;
    // siblings on the same parent get bumped along +X.
    public bool IsSubRoom { get; set; }
    public string? ParentRoomId { get; set; }
    public List<FloorDoorState> Doors { get; set; } = new();
    public List<FloorEnemyEntry> Enemies { get; set; } = new();
    public List<FloorLootEntry> Lootables { get; set; } = new();
}

public sealed class FloorDoorState
{
    public string? Direction { get; set; }
    public string? ToRoomId { get; set; }
    public bool Locked { get; set; }
    public string? KeyId { get; set; }
}

public sealed class FloorEnemyEntry
{
    public string? EnemyId { get; set; }
    public int Count { get; set; }
    public string? Source { get; set; }
}

public sealed class FloorLootEntry
{
    public string? ItemId { get; set; }
    public int Weight { get; set; }
    public int MinCount { get; set; }
    public int MaxCount { get; set; }
}
