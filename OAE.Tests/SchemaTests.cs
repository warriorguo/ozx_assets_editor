using System.Text.Json.Nodes;
using Game.Contracts.Data;
using OAE.Core.Schema;
using OAE.Core.Templates;

namespace OAE.Tests;

public class SchemaTests
{
    [Fact]
    public void EntityTypes_covers_all_22_buckets()
    {
        Assert.Equal(22, EntityTypes.Map.Count);
        // Subdir id == OAE type id, by convention.
        Assert.Contains("enemies", EntityTypes.Map.Keys);
        Assert.Equal(typeof(EnemyData), EntityTypes.Map["enemies"]);
        Assert.Equal(typeof(WeaponData), EntityTypes.Map["weapons"]);
        Assert.Equal(typeof(SkillData), EntityTypes.Map["skills"]);
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
