namespace OAE.Core.Store;

/// <summary>
/// <see cref="IStore"/> wrapper that lets the UI swap the inner backend when
/// the user picks a different project folder, without tearing in-flight calls.
/// </summary>
public sealed class HotSwapStore : IStore
{
    private readonly Lock _lock = new();
    private IStore _inner;

    public HotSwapStore(IStore initial) => _inner = initial;

    /// <summary>
    /// Atomically replace the inner store. Returns the previous inner so the
    /// caller can dispose or release it.
    /// </summary>
    public IStore Swap(IStore next)
    {
        lock (_lock)
        {
            var prev = _inner;
            _inner = next;
            return prev;
        }
    }

    public IStore Inner
    {
        get { lock (_lock) return _inner; }
    }

    public StoreHealth HealthCheck() => Inner.HealthCheck();
    public IReadOnlyList<EntityDescriptor> List(string entityType) => Inner.List(entityType);
    public string Get(string entityType, string id) => Inner.Get(entityType, id);
    public string Create(string entityType, string id, string json) => Inner.Create(entityType, id, json);
    public string Update(string entityType, string id, string json) => Inner.Update(entityType, id, json);
    public void Delete(string entityType, string id) => Inner.Delete(entityType, id);
}
