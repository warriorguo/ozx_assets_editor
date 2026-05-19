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

        // ── bosses ─────────────────────────────────────────────────────────
        [(typeof(BossData), "aiProfileId")]                     = new(RefTarget: "ai"),
        [(typeof(BossData), "dropTableId")]                     = new(RefTarget: "loot_tables"),
        [(typeof(BossData), "spriteKeys[]")]                    = new(AssetKey: "enemy-sprite"),
        [(typeof(BossData), "phases[].summonPlanId")]           = new(RefTarget: "spawn_plans"),
        // phases[].addSkills[].skillId is declared as SkillBinding[] but the
        // necromancer.json encodes plain strings (OZX-380). The metadata is
        // wired here so the picker works once the upstream data is fixed.
        [(typeof(BossData), "phases[].addSkills[].skillId")]    = new(RefTarget: "skills"),

        // ── spawn_plans ────────────────────────────────────────────────────
        [(typeof(SpawnPlanData), "waves[].entries[].enemyId")]  = new(RefTarget: "enemies"),

        // ── cargo / oiltank ────────────────────────────────────────────────
        [(typeof(CargoData), "lootTableId")]                    = new(RefTarget: "loot_tables"),
        [(typeof(OilTankData), "lootTableId")]                  = new(RefTarget: "loot_tables"),

        // ── level_plans ────────────────────────────────────────────────────
        [(typeof(LevelBasePlanData), "floors[].stageTypes[].spawnPlanId")] = new(RefTarget: "spawn_plans"),
        [(typeof(LevelBasePlanData), "floors[].stageTypes[].lootPlanId")]  = new(RefTarget: "loot_tables"),

        // ── levels ─────────────────────────────────────────────────────────
        // floors[].rooms[].templateId references the rooms entity bucket
        // (room templates), not in-file room ids. startRoomId / bossRoomId
        // are scoped within the level itself — not annotated.
        [(typeof(LevelData), "floors[].rooms[].templateId")]    = new(RefTarget: "rooms"),
        [(typeof(LevelData), "floors[].rooms[].spawnPlanId")]   = new(RefTarget: "spawn_plans"),
        [(typeof(LevelData), "floors[].rooms[].lootPlanId")]    = new(RefTarget: "loot_tables"),

        // ── player ─────────────────────────────────────────────────────────
        [(typeof(PlayerData), "headId")]                        = new(RefTarget: "heads"),
        [(typeof(PlayerData), "startingSkills[].skillId")]      = new(RefTarget: "skills"),

        // OAE-7/OAE-9 field-level audit notes:
        //   - WeaponData.lightningId, fireSoundId, spriteKeys[] — no matching
        //     entity-type bucket or import-asset pipeline exists today.
        //   - ProjectileData.impactFxKey, BeamData.{muzzle,impact}FxKey —
        //     custom 'fx/key#ChildId@RRGGBB' format with embedded params; no
        //     pipeline. Treat as opaque.
        //   - ItemData.refId, LootTableData.entries[].itemId — cross-type
        //     (legs/skills/items depending on a sibling discriminator). The
        //     OAE-6 v1 picker handles single-type only; cross-type lands in
        //     a follow-up.
        //   - EnemyData.attackSetId — no attack-set entity bucket exists.
        //   - RoomData.prefabKey / tilemapKey — Unity asset paths with no
        //     ResourcesDB or import-asset pipeline mapping.
        //   - PlayerData.animConfigKey — could be enemy-sprite but player's
        //     sprite pipeline has different conventions; left opaque.
    };

    public static EditorMeta? For(Type rootType, string jsonPath) =>
        Map.TryGetValue((rootType, jsonPath), out var m) ? m : null;
}
