using Game.Contracts.Combat;
using Game.Contracts.Data;

namespace OAE.Core.Schema;

/// <summary>
/// The single source of truth for what OAE considers an "entity type": the
/// subdir under <c>GameData/</c>, and the <see cref="System.Type"/> the JSON
/// in that subdir deserialises into.
/// </summary>
/// <remarks>
/// Subdir name == OAE-facing type id, by convention. <see cref="Store.FsStore"/>
/// derives its bucket map from this dictionary so the layout of the editor
/// can never drift from the on-disk layout.
/// </remarks>
public static class EntityTypes
{
    public static readonly IReadOnlyDictionary<string, Type> Map = new Dictionary<string, Type>
    {
        ["ai"]          = typeof(AIProfileData),
        ["beams"]       = typeof(BeamData),
        ["bosses"]      = typeof(BossData),
        ["box_rarity"]  = typeof(BoxRarityConfigData),
        ["cargo"]       = typeof(CargoData),
        ["combat"]      = typeof(ContactCombatTuning),
        ["enemies"]     = typeof(EnemyData),
        ["heads"]       = typeof(HeadData),
        ["items"]       = typeof(ItemData),
        ["legs"]        = typeof(LegData),
        ["level_plans"] = typeof(LevelBasePlanData),
        ["levels"]      = typeof(LevelData),
        ["loot_tables"] = typeof(LootTableData),
        ["oiltank"]     = typeof(OilTankData),
        ["player"]      = typeof(PlayerData),
        ["progression"] = typeof(ProgressionData),
        ["projectiles"] = typeof(ProjectileData),
        ["rooms"]       = typeof(RoomData),
        ["shields"]     = typeof(ShieldData),
        ["skills"]      = typeof(SkillData),
        ["spawn_plans"] = typeof(SpawnPlanData),
        ["weapons"]     = typeof(WeaponData),
    };

    /// <summary>
    /// Reverse map: C# type -> OAE type id. Useful when resolving a nested
    /// reference whose type is known at compile time.
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, string> ByClrType =
        Map.ToDictionary(kv => kv.Value, kv => kv.Key);
}
