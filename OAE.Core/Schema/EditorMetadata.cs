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
        // NOTE: aiProfileId + the "ai" bucket were removed when ozx_base deleted the
        // dead AI-profile system (3211ed71). Enemy behavior is driven by the state
        // machine + passive/idleBehavior/attackType, not a data-driven profile.
        [(typeof(EnemyData), "spawnConfig.enemyIds[]")]  = new(RefTarget: "enemies"),
        [(typeof(EnemyData), "skills[].skillId")]        = new(RefTarget: "skills"),
        // OZX-528/535 / OAE-43: an elite rolls one skill from this pool at spawn.
        // Same SkillBinding.skillId shape as skills[] above → skills bucket.
        [(typeof(EnemyData), "elite.skillPool[].skillId")] = new(RefTarget: "skills"),
        [(typeof(EnemyData), "animConfigKey")]           = new(AssetKey: "enemy-sprite"),
        [(typeof(EnemyData), "spriteKeys[]")]            = new(AssetKey: "enemy-sprite"),
        // OZX-533 / OAE-44: enemies that deploy a moving-puddle hazard reference a
        // PuddleData id — resolve it against the puddles bucket.
        [(typeof(EnemyData), "puddleConfig.puddleId")]   = new(RefTarget: "puddles"),
        // OZX-546 / OAE-45,47: enemy creep is opt-in via this flag. JsonUtility
        // default-constructs an omitted creepConfig to a non-null instance, so a
        // bare presence check would make every enemy grow creep — the enabled bool
        // is the real discriminator. Authored creep enemies MUST set it true.
        [(typeof(EnemyData), "creepConfig.enabled")]     = new(
            Description: "Opt-in gate for enemy creep. Must be true for this enemy to grow creep on death; an omitted creepConfig deserializes to a non-null default, so this flag (not object presence) decides."),

        // ── weapons ────────────────────────────────────────────────────────
        // No weapon-sprite pipeline exists yet; spriteKeys stays a plain string
        // until that pipeline is authored under import-asset.
        [(typeof(WeaponData), "projectileId")]           = new(RefTarget: "projectiles"),
        [(typeof(WeaponData), "beamId")]                 = new(RefTarget: "beams"),
        // 'sounds' is a virtual ref-target backed by SoundConfigStore (OAE-11).
        // ReferenceIndex.Rebuild merges SoundConfigStore.List() under this id.
        [(typeof(WeaponData), "fireSoundId")]            = new(RefTarget: "sounds"),

        // ── skills ─────────────────────────────────────────────────────────
        [(typeof(SkillData), "effectsByLevel[].projectileId")] = new(RefTarget: "projectiles"),
        [(typeof(SkillData), "effectsByLevel[].shieldId")]     = new(RefTarget: "shields"),
        [(typeof(SkillData), "iconKeys[]")]                    = new(AssetKey: "skill-sprite"),
        [(typeof(SkillData), "groundEffectKeys[]")]            = new(AssetKey: "effect-sprite"),

        // ── items ──────────────────────────────────────────────────────────
        [(typeof(ItemData), "spriteKey")] = new(AssetKey: "item-icon"),

        // ── bosses ─────────────────────────────────────────────────────────
        // aiProfileId removed alongside the dead AI-profile system (see enemies).
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
        // OZX-474 / OAE-37: CargoData.lootTableId removed — loot is now decided
        // by the spawn context (room.floorLootTableId, FloorLootPlan, or the
        // staticPlacements[] override below), not by the box. No mapping here.
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
        // OZX-471 / OAE-36: designer-authored static placements per room. The
        // adapter switches on `kind`; the picker-ready fields below are only
        // meaningful for specific kinds, but the metadata makes the dropdowns
        // available unconditionally — the form already gates by string presence.
        [(typeof(LevelData), "floors[].rooms[].staticPlacements[].lootTableId")] = new(RefTarget: "loot_tables"),
        // OZX-489 / OAE-42: kind="skill" platform. skillId must reference a
        // passive SkillData id.
        [(typeof(LevelData), "floors[].rooms[].staticPlacements[].skillId")]     = new(RefTarget: "skills"),

        // ── room backgrounds (OAE-49,50,51,52) ──────────────────────────────
        // These fields exist on BOTH the standalone RoomData ("rooms" bucket)
        // and RoomNodeData inside LevelData ("levels" bucket, floors[].rooms[]).
        // Annotated on both so the description shows wherever a room is edited.

        // OZX-577,578 / OAE-49,50: per-room orientation override.
        [(typeof(RoomData), "background")] = new(Description: BackgroundOrientationHelp),
        [(typeof(LevelData), "floors[].rooms[].background")] = new(Description: BackgroundOrientationHelp),
        // OZX-581 / OAE-51: screen-fixed background behind the room.
        [(typeof(RoomData), "fixedBackground")] = new(Description: FixedBackgroundHelp),
        [(typeof(LevelData), "floors[].rooms[].fixedBackground")] = new(Description: FixedBackgroundHelp),
        // OZX-582,583 / OAE-52: moving distant-view background travel axis.
        [(typeof(RoomData), "movingBackground.direction")] = new(Description: MovingBackgroundDirectionHelp),
        [(typeof(LevelData), "floors[].rooms[].movingBackground.direction")] = new(Description: MovingBackgroundDirectionHelp),

        // ── backgrounds (OAE-48) ─────────────────────────────────────────────
        // OZX-547/548: per-background additive glow decals. lights[].type is a
        // color/style id the renderer resolves as ResourcesDB key "light/{type}".
        [(typeof(BackgroundLightData), "lights[].type")] = new(
            Description: "Glow color/style id. The renderer resolves it as ResourcesDB key 'light/<type>' (e.g. 'blue_spot' → 'light/blue_spot')."),

        // ── player ─────────────────────────────────────────────────────────
        [(typeof(PlayerData), "headId")]                        = new(RefTarget: "heads"),
        [(typeof(PlayerData), "startingSkills[].skillId")]      = new(RefTarget: "skills"),
        // OZX-377: linear-ramp movement feel. Sit next to moveSpeed in
        // PlayerStats declaration order, so they render as a movement-feel group.
        [(typeof(PlayerData), "stats.acceleration")] = new(
            Description: "units/sec² ramp toward target velocity (input × moveSpeed). Higher = snappier startup and direction reversal."),
        [(typeof(PlayerData), "stats.deceleration")] = new(
            Description: "units/sec² ramp toward zero when input is released. Higher = crisper stop."),

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

    // Shared help strings for the room-background fields (OAE-49..52). Kept as
    // consts so the standalone-room and in-level-room annotations stay in sync.
    private const string BackgroundOrientationHelp =
        "Optional background orientation override. Tokens match the background sprite-name suffix: " +
        "Top, TopBottom, LeftRight, All. Single-edge Top art is reused for bottom-opening rooms by " +
        "rotating the background transform 180° — there is no separate Bottom asset (OZX-577). " +
        "Empty = default openDoors-driven selection.";

    private const string FixedBackgroundHelp =
        "Optional ResourcesDB key of a screen-fixed background rendered behind the room background " +
        "(convention: 'fixedbackground/<name>', e.g. 'fixedbackground/galaxy'; OZX-581). " +
        "Empty = this room shows no fixed background.";

    private const string MovingBackgroundDirectionHelp =
        "Travel axis for the moving distant-view background (cars crossing the viewport): " +
        "'left-right' (horizontal) or 'top-bottom' (vertical). Only meaningful when " +
        "movingBackground.assetKey is set (the discriminator that enables the effect).";

    public static EditorMeta? For(Type rootType, string jsonPath) =>
        Map.TryGetValue((rootType, jsonPath), out var m) ? m : null;
}
