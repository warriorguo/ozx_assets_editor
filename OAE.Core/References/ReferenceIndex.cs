using OAE.Core.Schema;
using OAE.Core.Store;

namespace OAE.Core.References;

/// <summary>
/// Caches <c>{ typeId -&gt; ordered ids }</c> for the open project so the
/// reference picker and broken-ref checks can run without hitting the store
/// on every keystroke. Rebuild on project swap, on Save, and after import.
/// </summary>
public sealed class ReferenceIndex
{
    private readonly Dictionary<string, List<string>> _idsByType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _setByType = new(StringComparer.Ordinal);

    /// <summary>
    /// Rebuild every known entity type's id list from <paramref name="store"/>.
    /// Idempotent — clears prior state before rebuilding.
    /// </summary>
    public void Rebuild(IStore store)
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
    }

    public IReadOnlyList<string> IdsOf(string typeId) =>
        _idsByType.TryGetValue(typeId, out var ids) ? ids : Array.Empty<string>();

    public bool Contains(string typeId, string id) =>
        _setByType.TryGetValue(typeId, out var set) && set.Contains(id);
}
