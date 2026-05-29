using Game.Contracts.Combat;
using Game.Contracts.Data;

namespace OAE.Core.Schema;

/// <summary>
/// The single source of truth for what OAE considers an "entity type": the
/// OAE-facing id, the <see cref="System.Type"/> the JSON deserialises into,
/// and the on-disk subdir under <c>GameData/</c> the files live in.
/// </summary>
/// <remarks>
/// <para>By default the on-disk subdir equals the OAE-facing entity type id
/// (e.g. <c>enemies</c>, <c>weapons</c>). <see cref="Subdirs"/> overrides this
/// when multiple entity types share a single physical directory; in that case
/// the <c>dataType</c> JSON field discriminates which entity type a given file
/// belongs to. <see cref="Store.FsStore"/> derives its bucket map from
/// <see cref="SubdirOf"/> and filters file listings by dataType when a subdir
/// is shared (OAE-32).</para>
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
    /// OAE-32: overrides the on-disk subdir when an entity type does NOT live
    /// in a subdir named after itself. Used for shared subdirs where multiple
    /// entity types co-exist and are discriminated by the JSON <c>dataType</c>
    /// field. Entries omitted here default to <c>entityType == subdir name</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Subdirs = new Dictionary<string, string>
    {
        // OZX-443: level_plans (LevelBasePlanData) lives alongside levels (LevelData)
        // under GameData/levels/. The two are distinguished by their dataType field.
        ["level_plans"] = "levels",
    };

    /// <summary>
    /// Returns the on-disk subdir under <c>GameData/</c> that holds entities of
    /// <paramref name="entityType"/>. Defaults to <paramref name="entityType"/>
    /// itself unless overridden in <see cref="Subdirs"/>.
    /// </summary>
    public static string SubdirOf(string entityType) =>
        Subdirs.TryGetValue(entityType, out var s) ? s : entityType;

    /// <summary>
    /// Returns the <c>dataType</c> string expected in JSON files of
    /// <paramref name="entityType"/>. Convention: equals the simple CLR type
    /// name (e.g. entityType <c>levels</c> → dataType <c>"LevelData"</c>).
    /// </summary>
    public static string DataTypeOf(string entityType) =>
        Map.TryGetValue(entityType, out var t)
            ? t.Name
            : throw new ArgumentException($"unknown entity type: {entityType}", nameof(entityType));

    /// <summary>
    /// True if the on-disk subdir for <paramref name="entityType"/> is shared
    /// with at least one other entity type. Callers must filter file listings
    /// by <c>dataType</c> in that case.
    /// </summary>
    public static bool IsSharedSubdir(string entityType)
    {
        var subdir = SubdirOf(entityType);
        var count = 0;
        foreach (var key in Map.Keys)
            if (SubdirOf(key) == subdir && ++count > 1) return true;
        return false;
    }

    /// <summary>
    /// Reverse map: C# type -> OAE type id. Useful when resolving a nested
    /// reference whose type is known at compile time.
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, string> ByClrType =
        Map.ToDictionary(kv => kv.Value, kv => kv.Key);
}
