using Game.Contracts.Data;

namespace OAE.Core.Schema;

/// <summary>
/// Editor-only metadata that the form layer (OAE-5) and reference picker
/// (OAE-6) need to render a field intelligently — but which the runtime
/// game code doesn't care about. Kept here so OAE never needs to modify
/// ozx_base sources.
/// </summary>
public sealed record EditorMeta(
    string? RefTarget = null,
    string? AssetKey = null,
    string? Description = null);

/// <summary>
/// Side-table keyed by (root entity type, dotted JSON path) -> editor meta.
/// Lookup happens during <see cref="SchemaBuilder.Build"/> and is also
/// exposed for code paths that have a path string in hand.
/// </summary>
public static class EditorMetadata
{
    private static readonly Dictionary<(Type, string), EditorMeta> Map = new()
    {
        // ── enemies ────────────────────────────────────────────────────────
        [(typeof(EnemyData), "projectileId")]            = new(RefTarget: "projectiles"),
        [(typeof(EnemyData), "dropTableId")]             = new(RefTarget: "loot_tables"),
        [(typeof(EnemyData), "aiProfileId")]             = new(RefTarget: "ai"),
        [(typeof(EnemyData), "spawnConfig.enemyIds[]")]  = new(RefTarget: "enemies"),
        [(typeof(EnemyData), "skills[].skillId")]        = new(RefTarget: "skills"),
        [(typeof(EnemyData), "animConfigKey")]           = new(AssetKey: "resourcesdb"),
        [(typeof(EnemyData), "spriteKeys[]")]            = new(AssetKey: "resourcesdb"),

        // ── weapons ────────────────────────────────────────────────────────
        [(typeof(WeaponData), "projectileId")]           = new(RefTarget: "projectiles"),
        [(typeof(WeaponData), "beamId")]                 = new(RefTarget: "beams"),
        [(typeof(WeaponData), "spriteKeys[]")]           = new(AssetKey: "resourcesdb"),

        // ── skills ─────────────────────────────────────────────────────────
        [(typeof(SkillData), "effectsByLevel[].projectileId")] = new(RefTarget: "projectiles"),
        [(typeof(SkillData), "effectsByLevel[].shieldId")]     = new(RefTarget: "shields"),
        [(typeof(SkillData), "iconKeys[]")]                    = new(AssetKey: "resourcesdb"),
        [(typeof(SkillData), "groundEffectKeys[]")]            = new(AssetKey: "resourcesdb"),

        // OAE-7 will fill in projectiles / items / loot_tables / spawn_plans / rooms / levels.
    };

    public static EditorMeta? For(Type rootType, string jsonPath) =>
        Map.TryGetValue((rootType, jsonPath), out var m) ? m : null;
}
