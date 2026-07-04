using System.Text.Json.Nodes;
using Game.Contracts.Data;
using OAE.Core.Schema;
using OAE.Core.Templates;

namespace OAE.Tests;

public class SchemaTests
{
    [Fact]
    public void EntityTypes_covers_all_expected_buckets()
    {
        // 22 original buckets − ai (dead AI-profile system removed upstream,
        // 3211ed71) + puddles (OAE-44) + backgrounds (OAE-48) + name_libraries
        // (OAE-53) = 24. New buckets bump this count and must ship a template
        // (see TemplateLoader_has_at_least_one_template_for_every_type).
        Assert.Equal(24, EntityTypes.Map.Count);
        Assert.DoesNotContain("ai", EntityTypes.Map.Keys);
        // Subdir id == OAE type id, by convention.
        Assert.Contains("enemies", EntityTypes.Map.Keys);
        Assert.Equal(typeof(EnemyData), EntityTypes.Map["enemies"]);
        Assert.Equal(typeof(WeaponData), EntityTypes.Map["weapons"]);
        Assert.Equal(typeof(SkillData), EntityTypes.Map["skills"]);
        Assert.Equal(typeof(PuddleData), EntityTypes.Map["puddles"]);
        Assert.Equal(typeof(BackgroundLightData), EntityTypes.Map["backgrounds"]);
        Assert.Equal(typeof(NameLibraryData), EntityTypes.Map["name_libraries"]);
    }

    [Fact]
    public void EntityTypes_reverse_map_is_consistent()
    {
        Assert.Equal(EntityTypes.Map.Count, EntityTypes.ByClrType.Count);
        Assert.Equal("enemies", EntityTypes.ByClrType[typeof(EnemyData)]);
    }

    [Fact]
    public void SchemaBuilder_walks_EnemyData_top_level_fields()
    {
        var schema = SchemaBuilder.For<EnemyData>();
        Assert.Equal(typeof(EnemyData), schema.Type);

        var byName = schema.Fields.ToDictionary(f => f.Name);
        Assert.Contains("id", byName.Keys);
        Assert.Contains("displayName", byName.Keys);
        Assert.Contains("stats", byName.Keys);
        Assert.Contains("attackType", byName.Keys);
        Assert.Contains("dropTableId", byName.Keys);
        Assert.Contains("projectileId", byName.Keys);
        Assert.Contains("weaponConfig", byName.Keys);
        Assert.Contains("spawnConfig", byName.Keys);
        Assert.Contains("skills", byName.Keys);

        Assert.Equal(FieldKind.String, byName["id"].Kind);
        Assert.Equal(FieldKind.Object, byName["stats"].Kind);
        Assert.Equal(FieldKind.Object, byName["weaponConfig"].Kind);
        Assert.Equal(FieldKind.Array, byName["skills"].Kind);
        Assert.NotNull(byName["skills"].Element);
    }

    [Fact]
    public void SchemaBuilder_descends_into_nested_objects()
    {
        var schema = SchemaBuilder.For<EnemyData>();
        var stats = schema.Fields.First(f => f.Name == "stats");
        Assert.NotNull(stats.Nested);
        var statsByName = stats.Nested!.Fields.ToDictionary(f => f.Name);
        Assert.Contains("hp", statsByName.Keys);
        Assert.Contains("moveSpeed", statsByName.Keys);
        Assert.Equal(FieldKind.Float, statsByName["hp"].Kind);
    }

    [Fact]
    public void SchemaBuilder_descends_into_array_element_objects()
    {
        var schema = SchemaBuilder.For<EnemyData>();
        var skills = schema.Fields.First(f => f.Name == "skills");
        Assert.Equal(FieldKind.Array, skills.Kind);
        var element = skills.Element!;
        Assert.Equal(FieldKind.Object, element.Kind);
        Assert.NotNull(element.Nested);
        var skillIdField = element.Nested!.Fields.FirstOrDefault(f => f.Name == "skillId");
        Assert.NotNull(skillIdField);
        Assert.Equal(FieldKind.String, skillIdField!.Kind);
    }

    [Fact]
    public void SchemaBuilder_succeeds_for_every_entity_type()
    {
        foreach (var (typeId, clrType) in EntityTypes.Map)
        {
            var schema = SchemaBuilder.For(clrType);
            Assert.NotNull(schema);
            Assert.NotEmpty(schema.Fields);
        }
    }

    [Fact]
    public void SchemaBuilder_caches_per_type()
    {
        var a = SchemaBuilder.For<EnemyData>();
        var b = SchemaBuilder.For<EnemyData>();
        Assert.Same(a, b);
    }

    [Fact]
    public void EditorMetadata_resolves_top_level_ref()
    {
        var meta = EditorMetadata.For(typeof(EnemyData), "projectileId");
        Assert.NotNull(meta);
        Assert.Equal("projectiles", meta!.RefTarget);
    }

    [Fact]
    public void EditorMetadata_resolves_nested_array_ref()
    {
        var meta = EditorMetadata.For(typeof(EnemyData), "spawnConfig.enemyIds[]");
        Assert.NotNull(meta);
        Assert.Equal("enemies", meta!.RefTarget);
    }

    [Fact]
    public void EditorMetadata_returns_null_for_unknown_path()
    {
        Assert.Null(EditorMetadata.For(typeof(EnemyData), "nope.not.real"));
    }

    [Fact]
    public void PlayerStats_exposes_acceleration_and_deceleration_as_floats_adjacent_to_moveSpeed()
    {
        // OAE-19: surfacing the OZX-377 movement-feel fields. The editor relies
        // on PlayerStats declaration order to group them with moveSpeed — lock
        // that in so a reorder upstream doesn't silently scatter the form.
        var schema = SchemaBuilder.For<PlayerData>();
        var stats = schema.Fields.First(f => f.Name == "stats");
        Assert.NotNull(stats.Nested);

        var names = stats.Nested!.Fields.Select(f => f.Name).ToList();
        var moveSpeedIdx = names.IndexOf("moveSpeed");
        Assert.True(moveSpeedIdx >= 0, "PlayerStats must have moveSpeed");
        Assert.Equal("acceleration", names[moveSpeedIdx + 1]);
        Assert.Equal("deceleration", names[moveSpeedIdx + 2]);

        var byName = stats.Nested!.Fields.ToDictionary(f => f.Name);
        Assert.Equal(FieldKind.Float, byName["acceleration"].Kind);
        Assert.Equal(FieldKind.Float, byName["deceleration"].Kind);
    }

    [Fact]
    public void EditorMetadata_describes_acceleration_and_deceleration()
    {
        var accel = EditorMetadata.For(typeof(PlayerData), "stats.acceleration");
        Assert.NotNull(accel);
        Assert.False(string.IsNullOrWhiteSpace(accel!.Description));

        var decel = EditorMetadata.For(typeof(PlayerData), "stats.deceleration");
        Assert.NotNull(decel);
        Assert.False(string.IsNullOrWhiteSpace(decel!.Description));
    }

    [Fact]
    public void CargoData_has_no_lootTableId_after_OZX_474()
    {
        // OZX-474 / OAE-37: cargo describes the BOX only; loot is decided by
        // the spawn context, not the box. The EditorMetadata entry for it was
        // removed at the same time — check both ends.
        var schema = SchemaBuilder.For<CargoData>();
        Assert.DoesNotContain(schema.Fields, f => f.Name == "lootTableId");
        Assert.Null(EditorMetadata.For(typeof(CargoData), "lootTableId"));
    }

    [Fact]
    public void RoomNodeData_exposes_staticPlacements_array_with_OZX_489_fields()
    {
        // OAE-36 / OAE-42: reflection picks up staticPlacements[] automatically.
        // Lock in the sub-schema fields that the editor / picker rely on.
        var schema = SchemaBuilder.For<LevelData>();
        var floors = schema.Fields.First(f => f.Name == "floors");
        var floorNested = floors.Element!.Nested!;
        var rooms = floorNested.Fields.First(f => f.Name == "rooms");
        var roomNested = rooms.Element!.Nested!;

        var sp = roomNested.Fields.FirstOrDefault(f => f.Name == "staticPlacements");
        Assert.NotNull(sp);
        Assert.Equal(FieldKind.Array, sp!.Kind);
        var entry = sp.Element!.Nested!;
        var byName = entry.Fields.ToDictionary(f => f.Name);
        Assert.Contains("kind", byName.Keys);
        Assert.Contains("prefabKey", byName.Keys);
        Assert.Contains("count", byName.Keys);
        Assert.Contains("cells", byName.Keys);
        Assert.Contains("lootTableId", byName.Keys);
        Assert.Contains("skillId", byName.Keys);          // OZX-489
        Assert.Contains("buffDurationSeconds", byName.Keys); // OZX-489
    }

    [Fact]
    public void EditorMetadata_resolves_staticPlacements_ref_targets()
    {
        Assert.Equal("skills",
            EditorMetadata.For(typeof(LevelData), "floors[].rooms[].staticPlacements[].skillId")?.RefTarget);
        Assert.Equal("loot_tables",
            EditorMetadata.For(typeof(LevelData), "floors[].rooms[].staticPlacements[].lootTableId")?.RefTarget);
    }

    [Fact]
    public void EnemyData_exposes_elite_config_sub_schema()
    {
        // OZX-535 / OAE-43: EnemyData.elite (EliteConfig) is picked up by reflection.
        // Lock its shape so the form surfaces the stat-multiplier ranges + skill pool.
        var schema = SchemaBuilder.For<EnemyData>();
        var elite = schema.Fields.FirstOrDefault(f => f.Name == "elite");
        Assert.NotNull(elite);
        Assert.Equal(FieldKind.Object, elite!.Kind);

        var byName = elite.Nested!.Fields.ToDictionary(f => f.Name);
        Assert.Contains("statMultipliers", byName.Keys);
        Assert.Contains("skillPool", byName.Keys);
        Assert.Equal(FieldKind.Array, byName["statMultipliers"].Kind);
        Assert.Equal(FieldKind.Array, byName["skillPool"].Kind);

        // statMultipliers[] element carries the stat + min/max multiplier fields.
        var mult = byName["statMultipliers"].Element!.Nested!.Fields.ToDictionary(f => f.Name);
        Assert.Contains("stat", mult.Keys);
        Assert.Contains("minMultiplier", mult.Keys);
        Assert.Contains("maxMultiplier", mult.Keys);
        Assert.Equal(FieldKind.Float, mult["minMultiplier"].Kind);
    }

    [Fact]
    public void SpawnEntry_exposes_eliteCount_int()
    {
        // OZX-526 / OAE-43: SpawnEntry.eliteCount surfaces under waves[].entries[].
        var schema = SchemaBuilder.For<SpawnPlanData>();
        var waves = schema.Fields.First(f => f.Name == "waves");
        var entries = waves.Element!.Nested!.Fields.First(f => f.Name == "entries");
        var byName = entries.Element!.Nested!.Fields.ToDictionary(f => f.Name);
        Assert.Contains("eliteCount", byName.Keys);
        Assert.Equal(FieldKind.Int, byName["eliteCount"].Kind);
    }

    [Fact]
    public void EditorMetadata_resolves_elite_skillPool_ref_target()
    {
        Assert.Equal("skills",
            EditorMetadata.For(typeof(EnemyData), "elite.skillPool[].skillId")?.RefTarget);
    }

    [Fact]
    public void PuddleData_bucket_builds_a_schema_and_has_a_template()
    {
        // OAE-44: PuddleData is a first-class GameData entity type.
        var schema = SchemaBuilder.For<PuddleData>();
        var byName = schema.Fields.ToDictionary(f => f.Name);
        Assert.Contains("id", byName.Keys);
        Assert.Contains("prefabKey", byName.Keys);
        Assert.Contains("coreRadius", byName.Keys);
        Assert.Contains("damage", byName.Keys);
        Assert.Contains("targetLayer", byName.Keys);
        Assert.Equal(FieldKind.Float, byName["coreRadius"].Kind);

        var t = TemplateLoader.Get("puddles", "toxic");
        Assert.NotNull(t);
        var body = JsonNode.Parse(t!.Body)!.AsObject();
        Assert.Equal("PuddleData", body["dataType"]!.GetValue<string>());
    }

    [Fact]
    public void EditorMetadata_resolves_enemy_puddle_ref_target()
    {
        // OAE-44: enemy puddle deployers reference a PuddleData id.
        Assert.Equal("puddles",
            EditorMetadata.For(typeof(EnemyData), "puddleConfig.puddleId")?.RefTarget);
    }

    [Fact]
    public void EnemyData_exposes_creepConfig_sub_schema_with_enabled_gate()
    {
        // OZX-546 / OAE-45,47: EnemyData.creepConfig (CreepConfig) is picked up by
        // reflection; the enabled bool is the opt-in discriminator.
        var schema = SchemaBuilder.For<EnemyData>();
        var creep = schema.Fields.FirstOrDefault(f => f.Name == "creepConfig");
        Assert.NotNull(creep);
        Assert.Equal(FieldKind.Object, creep!.Kind);

        var byName = creep.Nested!.Fields.ToDictionary(f => f.Name);
        Assert.Contains("enabled", byName.Keys);
        Assert.Equal(FieldKind.Bool, byName["enabled"].Kind);
        Assert.Contains("maxDepth", byName.Keys);
        Assert.Contains("growInterval", byName.Keys);
        Assert.Contains("growPerTick", byName.Keys);
        Assert.Contains("growRadius", byName.Keys);
    }

    [Fact]
    public void EditorMetadata_describes_creepConfig_enabled_gate()
    {
        var meta = EditorMetadata.For(typeof(EnemyData), "creepConfig.enabled");
        Assert.NotNull(meta);
        Assert.False(string.IsNullOrWhiteSpace(meta!.Description));
    }

    [Fact]
    public void RoomData_exposes_background_orientation_fields()
    {
        // OZX-577,578,581 / OAE-49,50,51: per-room background override + fixed
        // background surface on the standalone RoomData bucket.
        var schema = SchemaBuilder.For<RoomData>();
        var byName = schema.Fields.ToDictionary(f => f.Name);
        Assert.Contains("background", byName.Keys);
        Assert.Equal(FieldKind.String, byName["background"].Kind);
        Assert.Contains("fixedBackground", byName.Keys);
        Assert.Equal(FieldKind.String, byName["fixedBackground"].Kind);
        Assert.Contains("movingBackground", byName.Keys);
        Assert.Equal(FieldKind.Object, byName["movingBackground"].Kind);
    }

    [Fact]
    public void RoomData_movingBackground_sub_schema_exposes_direction_and_density()
    {
        // OZX-582,583 / OAE-52: moving distant-view background config.
        var schema = SchemaBuilder.For<RoomData>();
        var moving = schema.Fields.First(f => f.Name == "movingBackground");
        var byName = moving.Nested!.Fields.ToDictionary(f => f.Name);
        Assert.Contains("assetKey", byName.Keys);
        Assert.Contains("direction", byName.Keys);
        Assert.Contains("density", byName.Keys);
        Assert.Contains("speed", byName.Keys);
        Assert.Equal(FieldKind.String, byName["direction"].Kind);
        Assert.Equal(FieldKind.Int, byName["density"].Kind);
    }

    [Fact]
    public void RoomNodeData_in_level_exposes_background_orientation_fields()
    {
        // OAE-49,50,51,52: same fields on RoomNodeData nested in LevelData.
        var schema = SchemaBuilder.For<LevelData>();
        var rooms = schema.Fields.First(f => f.Name == "floors")
                          .Element!.Nested!.Fields.First(f => f.Name == "rooms");
        var byName = rooms.Element!.Nested!.Fields.ToDictionary(f => f.Name);
        Assert.Contains("background", byName.Keys);
        Assert.Contains("fixedBackground", byName.Keys);
        Assert.Contains("movingBackground", byName.Keys);
    }

    [Theory]
    [InlineData("background")]
    [InlineData("fixedBackground")]
    [InlineData("movingBackground.direction")]
    public void EditorMetadata_describes_room_background_fields(string field)
    {
        // OAE-49..52: help text attached on both the standalone-room and the
        // in-level room paths.
        var onRoom = EditorMetadata.For(typeof(RoomData), field);
        Assert.NotNull(onRoom);
        Assert.False(string.IsNullOrWhiteSpace(onRoom!.Description));

        var onNode = EditorMetadata.For(typeof(LevelData), $"floors[].rooms[].{field}");
        Assert.NotNull(onNode);
        Assert.Equal(onRoom.Description, onNode!.Description);
    }

    [Fact]
    public void BackgroundLightData_bucket_builds_a_schema_and_has_a_template()
    {
        // OAE-48: per-background light layout is a first-class GameData entity type.
        var schema = SchemaBuilder.For<BackgroundLightData>();
        var byName = schema.Fields.ToDictionary(f => f.Name);
        Assert.Contains("id", byName.Keys);
        Assert.Contains("lights", byName.Keys);
        Assert.Equal(FieldKind.Array, byName["lights"].Kind);

        var light = byName["lights"].Element!.Nested!.Fields.ToDictionary(f => f.Name);
        Assert.Contains("x", light.Keys);
        Assert.Contains("y", light.Keys);
        Assert.Contains("rotationDeg", light.Keys);
        Assert.Contains("type", light.Keys);
        Assert.Contains("scale", light.Keys);
        Assert.Contains("alpha", light.Keys);

        var t = TemplateLoader.Get("backgrounds", "lights");
        Assert.NotNull(t);
        var body = JsonNode.Parse(t!.Body)!.AsObject();
        Assert.Equal("BackgroundLightData", body["dataType"]!.GetValue<string>());
    }

    [Fact]
    public void EditorMetadata_describes_background_light_type()
    {
        var meta = EditorMetadata.For(typeof(BackgroundLightData), "lights[].type");
        Assert.NotNull(meta);
        Assert.False(string.IsNullOrWhiteSpace(meta!.Description));
    }

    [Fact]
    public void NameLibraryData_bucket_builds_a_schema_and_has_a_template()
    {
        // OZX-586 / OAE-53: name-segment library is a first-class GameData type.
        var schema = SchemaBuilder.For<NameLibraryData>();
        var byName = schema.Fields.ToDictionary(f => f.Name);
        Assert.Contains("id", byName.Keys);
        Assert.Contains("pools", byName.Keys);
        Assert.Equal(FieldKind.Array, byName["pools"].Kind);

        var pool = byName["pools"].Element!.Nested!.Fields.ToDictionary(f => f.Name);
        Assert.Contains("key", pool.Keys);
        Assert.Contains("values", pool.Keys);
        Assert.Equal(FieldKind.Array, pool["values"].Kind);

        var t = TemplateLoader.Get("name_libraries", "basic");
        Assert.NotNull(t);
        var body = JsonNode.Parse(t!.Body)!.AsObject();
        Assert.Equal("NameLibraryData", body["dataType"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(typeof(WeaponData))]
    [InlineData(typeof(ItemData))]
    public void Equipment_templates_expose_nameTemplate_field(Type clrType)
    {
        // OZX-586 / OAE-53: equipment templates carry a nameTemplate that the
        // resolver expands against the name library. Reflection surfaces it.
        var schema = SchemaBuilder.For(clrType);
        var byName = schema.Fields.ToDictionary(f => f.Name);
        Assert.Contains("nameTemplate", byName.Keys);
        Assert.Equal(FieldKind.String, byName["nameTemplate"].Kind);
    }

    [Fact]
    public void Player_basic_template_carries_acceleration_and_deceleration_defaults()
    {
        var t = TemplateLoader.Get("player", "basic");
        Assert.NotNull(t);
        var body = JsonNode.Parse(t!.Body)!.AsObject();
        var stats = body["stats"]!.AsObject();
        Assert.True(stats.ContainsKey("acceleration"), "player template must seed acceleration");
        Assert.True(stats.ContainsKey("deceleration"), "player template must seed deceleration");
        Assert.True(stats["acceleration"]!.GetValue<float>() > 0);
        Assert.True(stats["deceleration"]!.GetValue<float>() > 0);
    }

    [Fact]
    public void SchemaBuilder_attaches_meta_to_top_level_field()
    {
        var schema = SchemaBuilder.For<EnemyData>();
        var projectileId = schema.Fields.First(f => f.Name == "projectileId");
        Assert.NotNull(projectileId.Meta);
        Assert.Equal("projectiles", projectileId.Meta!.RefTarget);
    }

    [Fact]
    public void TemplateLoader_finds_seed_templates()
    {
        var enemies = TemplateLoader.For("enemies");
        Assert.Contains(enemies, t => t.Id == "kamikaze");

        var weapons = TemplateLoader.For("weapons");
        Assert.Contains(weapons, t => t.Id == "ranged_auto");
    }

    [Theory]
    [InlineData("enemies", 3)]
    [InlineData("weapons", 3)]
    [InlineData("projectiles", 3)]
    [InlineData("skills", 3)]
    [InlineData("items", 3)]
    public void TemplateLoader_has_at_least_N_templates_per_P1_type(string typeId, int min)
    {
        var templates = TemplateLoader.For(typeId);
        Assert.True(templates.Count >= min,
            $"{typeId}: expected >= {min} templates, got {templates.Count}");
    }

    [Theory]
    [MemberData(nameof(AllTypeIds))]
    public void TemplateLoader_has_at_least_one_template_for_every_type(string typeId)
    {
        var templates = TemplateLoader.For(typeId);
        Assert.NotEmpty(templates);
    }

    public static IEnumerable<object[]> AllTypeIds() =>
        EntityTypes.Map.Keys.Select(k => new object[] { k });

    [Fact]
    public void TemplateLoader_parses_assetSlots_when_present()
    {
        var kamikaze = TemplateLoader.Get("enemies", "kamikaze");
        Assert.NotNull(kamikaze);
        Assert.NotEmpty(kamikaze!.AssetSlots);
        Assert.Equal("enemy-sprite", kamikaze.AssetSlots[0].Pipeline);
        Assert.Equal("animConfigKey", kamikaze.AssetSlots[0].Name);
    }

    [Fact]
    public void TemplateLoader_returns_empty_slots_when_field_absent()
    {
        // weapons/beam.json has no assetSlots field.
        var beam = TemplateLoader.Get("weapons", "beam");
        Assert.NotNull(beam);
        Assert.Empty(beam!.AssetSlots);
    }

    [Fact]
    public void TemplateLoader_body_is_parseable_json_and_carries_dataType()
    {
        var t = TemplateLoader.Get("enemies", "kamikaze");
        Assert.NotNull(t);
        var node = JsonNode.Parse(t!.Body)!.AsObject();
        Assert.Equal("EnemyData", node["dataType"]!.GetValue<string>());
        Assert.Equal("", node["id"]!.GetValue<string>()); // placeholder
    }

    [Fact]
    public void TemplateLoader_returns_empty_for_unknown_type()
    {
        Assert.Empty(TemplateLoader.For("not_a_real_type"));
    }
}
