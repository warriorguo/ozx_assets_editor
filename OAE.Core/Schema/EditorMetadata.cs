using Game.Contracts.Data;

namespace OAE.Core.Schema;

/// <summary>
/// Editor-only metadata that the form layer (OAE-5), reference picker
/// (OAE-6), and asset drop-zone (OAE-15) need to render a field
/// intelligently — but which the runtime game code doesn't care about.
/// Kept here so OAE never needs to modify ozx_base sources.
/// </summary>
/// <param name="RefTarget">
///   Entity type id (e.g. "projectiles") that this field references — drives
///   the cross-reference picker.
/// </param>
/// <param name="AssetKey">
///   Name of the <c>import-asset</c> pipeline (e.g. "enemy-sprite",
///   "item-icon") that knows how to ingest a dropped file for this field.
///   Drives the OAE-15 drop zone. <c>null</c> means the field is a plain
///   string with no asset-import affordance.
/// </param>
/// <param name="Description">Free-text help shown next to the field label.</param>
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
        // AssetKey values are pipeline names from
        // ~/.claude/skills/import-asset/pipelines/*.json. RefTarget values are
        // OAE entity-type ids (matching FsStore subdirs).

        // ── enemies ────────────────────────────────────────────────────────
        [(typeof(EnemyData), "projectileId")]            = new(RefTarget: "projectiles"),
        [(typeof(EnemyData), "dropTableId")]             = new(RefTarget: "loot_tables"),
        [(typeof(EnemyData), "aiProfileId")]             = new(RefTarget: "ai"),
        [(typeof(EnemyData), "spawnConfig.enemyIds[]")]  = new(RefTarget: "enemies"),
        [(typeof(EnemyData), "skills[].skillId")]        = new(RefTarget: "skills"),
        [(typeof(EnemyData), "animConfigKey")]           = new(AssetKey: "enemy-sprite"),
        [(typeof(EnemyData), "spriteKeys[]")]            = new(AssetKey: "enemy-sprite"),

        // ── weapons ────────────────────────────────────────────────────────
        // No weapon-sprite pipeline exists yet; spriteKeys stays a plain string
        // until that pipeline is authored under import-asset.
        [(typeof(WeaponData), "projectileId")]           = new(RefTarget: "projectiles"),
        [(typeof(WeaponData), "beamId")]                 = new(RefTarget: "beams"),

        // ── skills ─────────────────────────────────────────────────────────
        [(typeof(SkillData), "effectsByLevel[].projectileId")] = new(RefTarget: "projectiles"),
        [(typeof(SkillData), "effectsByLevel[].shieldId")]     = new(RefTarget: "shields"),
        [(typeof(SkillData), "iconKeys[]")]                    = new(AssetKey: "skill-sprite"),
        [(typeof(SkillData), "groundEffectKeys[]")]            = new(AssetKey: "effect-sprite"),

        // ── items ──────────────────────────────────────────────────────────
        [(typeof(ItemData), "spriteKey")] = new(AssetKey: "item-icon"),

        // OAE-7 will fill in projectiles / loot_tables / spawn_plans / rooms / levels.
    };

    public static EditorMeta? For(Type rootType, string jsonPath) =>
        Map.TryGetValue((rootType, jsonPath), out var m) ? m : null;
}
