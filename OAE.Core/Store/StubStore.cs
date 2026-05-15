namespace OAE.Core.Store;

/// <summary>
/// Placeholder used when no project root is mounted. Reads return empty,
/// writes throw. The real fsstore from OAE-3 replaces this at runtime via
/// <see cref="HotSwapStore"/>.
/// </summary>
public sealed class StubStore : IStore
{
    private readonly string _reason;

    public StubStore(string reason = "no project root mounted") => _reason = reason;

    public StoreHealth HealthCheck() => new(Ok: false, Reason: _reason);

    public IReadOnlyList<EntityDescriptor> List(string entityType) => Array.Empty<EntityDescriptor>();

    public string Get(string entityType, string id) => throw new EntityNotFoundException(entityType, id);

    public string Create(string entityType, string id, string json) => throw new StoreUnmountedException(_reason);
    public string Update(string entityType, string id, string json) => throw new StoreUnmountedException(_reason);
    public void Delete(string entityType, string id) => throw new StoreUnmountedException(_reason);
}
