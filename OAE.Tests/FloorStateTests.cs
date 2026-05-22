using System.Text.Json;
using OAE.Core.GameApi;

namespace OAE.Tests;

public class FloorStateTests
{
    // Mirrors the shape JsonConvert.SerializeObject produces server-side
    // (ozx_base/Assets/Scripts/Game.API/StateReader.cs + GameAPIServer.cs).
    private const string SampleJson = """
    {
      "FloorIndex": 0,
      "ThemeId": "factory",
      "StartRoomId": "r_start",
      "BossRoomId": "r_boss",
      "CurrentRoomId": "r_start",
      "Rooms": [
        {
          "RoomId": "r_start",
          "StageType": "start",
          "Category": "normal",
          "Shape": null,
          "BossId": null,
          "Cleared": false,
          "Visited": true,
          "HasTeleportSpot": false,
          "HasLayout": true,
          "GridX": 0,
          "GridY": 0,
          "Cols": 1,
          "Rows": 1,
          "SpawnPlanId": "sp_floor1",
          "LootPlanId": null,
          "Doors": [
            { "Direction": "Right", "ToRoomId": "r_mid", "Locked": false, "KeyId": null }
          ],
          "Enemies": [
            { "EnemyId": "skeleton", "Count": 2, "Source": "initial" },
            { "EnemyId": "bat", "Count": 3, "Source": "wave:w1" }
          ],
          "Lootables": [
            { "ItemId": "coin", "Weight": 10, "MinCount": 1, "MaxCount": 5 }
          ]
        },
        {
          "RoomId": "r_boss",
          "StageType": "boss",
          "Category": "normal",
          "Shape": null,
          "BossId": "boss_factory",
          "Cleared": false,
          "Visited": false,
          "HasTeleportSpot": true,
          "HasLayout": true,
          "GridX": 1,
          "GridY": 0,
          "Cols": 2,
          "Rows": 2,
          "SpawnPlanId": null,
          "LootPlanId": null,
          "Doors": [],
          "Enemies": [],
          "Lootables": []
        },
        {
          "RoomId": "r_cave_01",
          "StageType": "normal",
          "Category": "cave",
          "Shape": null,
          "BossId": null,
          "Cleared": false,
          "Visited": false,
          "HasTeleportSpot": false,
          "HasLayout": true,
          "GridX": 1,
          "GridY": -1,
          "Cols": 1,
          "Rows": 1,
          "IsSubRoom": true,
          "ParentRoomId": "r_start",
          "SpawnPlanId": null,
          "LootPlanId": null,
          "Doors": [],
          "Enemies": [],
          "Lootables": []
        }
      ]
    }
    """;

    [Fact]
    public void Deserialize_round_trips_all_documented_fields()
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var state = JsonSerializer.Deserialize<FloorState>(SampleJson, opts);
        Assert.NotNull(state);

        Assert.Equal(0, state!.FloorIndex);
        Assert.Equal("factory", state.ThemeId);
        Assert.Equal("r_start", state.StartRoomId);
        Assert.Equal("r_boss", state.BossRoomId);
        Assert.Equal("r_start", state.CurrentRoomId);
        Assert.Equal(3, state.Rooms.Count);

        var start = state.Rooms[0];
        Assert.Equal("r_start", start.RoomId);
        Assert.Equal("start", start.StageType);
        Assert.True(start.HasLayout);
        Assert.False(start.Cleared);
        Assert.True(start.Visited);
        Assert.Single(start.Doors);
        Assert.Equal("Right", start.Doors[0].Direction);
        Assert.Equal("r_mid", start.Doors[0].ToRoomId);
        Assert.Equal(2, start.Enemies.Count);
        Assert.Equal("skeleton", start.Enemies[0].EnemyId);
        Assert.Equal(2, start.Enemies[0].Count);
        Assert.Equal("initial", start.Enemies[0].Source);
        Assert.Equal("wave:w1", start.Enemies[1].Source);
        Assert.Single(start.Lootables);
        Assert.Equal(1, start.Lootables[0].MinCount);
        Assert.Equal(5, start.Lootables[0].MaxCount);

        var boss = state.Rooms[1];
        Assert.Equal("boss_factory", boss.BossId);
        Assert.True(boss.HasTeleportSpot);
        Assert.Equal(2, boss.Cols);
        Assert.Equal(2, boss.Rows);
        Assert.False(boss.IsSubRoom);
        Assert.Null(boss.ParentRoomId);

        // OZX-386: cave room is a sub-room anchored at parent+1 down-and-right.
        var cave = state.Rooms[2];
        Assert.Equal("r_cave_01", cave.RoomId);
        Assert.True(cave.IsSubRoom);
        Assert.Equal("r_start", cave.ParentRoomId);
        Assert.True(cave.HasLayout);
        Assert.Equal("cave", cave.Category);
    }

    [Fact]
    public void Deserialize_defaults_IsSubRoom_and_ParentRoomId_for_main_floor_rooms()
    {
        // Main-floor rooms in the response shape don't carry the sub-room
        // fields; defaults should be false / null.
        const string json = """
        { "Rooms": [ { "RoomId": "r_main", "HasLayout": true } ] }
        """;
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var state = JsonSerializer.Deserialize<FloorState>(json, opts);
        Assert.NotNull(state);
        Assert.Single(state!.Rooms);
        Assert.False(state.Rooms[0].IsSubRoom);
        Assert.Null(state.Rooms[0].ParentRoomId);
    }

    [Fact]
    public void Deserialize_tolerates_lowercase_property_names()
    {
        // Defensive: PropertyNameCaseInsensitive should accept either casing
        // if the OZX server ever switches to a camelCase serializer.
        const string json = """
        { "floorIndex": 3, "rooms": [ { "roomId": "x", "hasLayout": false } ] }
        """;
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var state = JsonSerializer.Deserialize<FloorState>(json, opts);
        Assert.NotNull(state);
        Assert.Equal(3, state!.FloorIndex);
        Assert.Single(state.Rooms);
        Assert.Equal("x", state.Rooms[0].RoomId);
        Assert.False(state.Rooms[0].HasLayout);
    }
}
