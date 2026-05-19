using OAE.Core.Schema;
using OAE.Core.Store;

namespace OAE.Core.References;

/// <summary>
/// Caches <c>{ typeId -&gt; ordered ids }</c> for the open project so the
/// reference picker and broken-ref checks can run without hitting the store
/// on every keystroke. Rebuild on project swap, on Save, and after import.
/// </summary>
/// <remarks>
/// <para>Indexes both <em>entity-type</em> ids (from <see cref="EntityTypes.Map"/>
/// via the store) and <em>virtual-type</em> ids registered by code paths that
/// own non-entity data (e.g. <c>sounds</c> from <c>SoundConfigStore</c>).
/// Virtual-type ids are passed in on each <see cref="Rebuild"/> via
/// <see cref="RebuildOptions"/>; they're cleared on every rebuild so callers
/// can re-supply current data.</para>
/// </remarks>
public sealed class ReferenceIndex
{
    private readonly Dictionary<string, List<string>> _idsByType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _setByType = new(StringComparer.Ordinal);

    /// <summary>
    /// Extra virtual-type id lists to merge into the index alongside the
    /// entity-type buckets. Keyed by virtual-type id (e.g. "sounds").
    /// </summary>
    public sealed record RebuildOptions(IReadOnlyDictionary<string, IReadOnlyList<string>>? VirtualTypes = null);

    /// <summary>
    /// Rebuild every known entity type's id list from <paramref name="store"/>,
    /// plus any virtual-type id lists supplied in <paramref name="options"/>.
    /// Idempotent — clears prior state before rebuilding.
    /// </summary>
    public void Rebuild(IStore store, RebuildOptions? options = null)
    {
        _idsByType.Clear();
        _setByType.Clear();
        foreach (var typeId in EntityTypes.Map.Keys)
        {
            var ids = new List<string>();
            try
            {
                foreach (var e in store.List(typeId)) ids.Add(e.Id);
            }
            catch { /* store may be a StubStore — leave the list empty */ }
            ids.Sort(StringComparer.Ordinal);
            _idsByType[typeId] = ids;
            _setByType[typeId] = new HashSet<string>(ids, StringComparer.Ordinal);
        }

        if (options?.VirtualTypes is { } virtuals)
        {
            foreach (var (typeId, ids) in virtuals)
            {
                var sorted = new List<string>(ids);
                sorted.Sort(StringComparer.Ordinal);
                _idsByType[typeId] = sorted;
                _setByType[typeId] = new HashSet<string>(sorted, StringComparer.Ordinal);
            }
        }
    }

    public IReadOnlyList<string> IdsOf(string typeId) =>
        _idsByType.TryGetValue(typeId, out var ids) ? ids : Array.Empty<string>();

    public bool Contains(string typeId, string id) =>
        _setByType.TryGetValue(typeId, out var set) && set.Contains(id);
}
