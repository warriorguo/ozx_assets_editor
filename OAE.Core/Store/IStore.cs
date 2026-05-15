namespace OAE.Core.Store;

/// <summary>
/// Lightweight description of an entity used by <see cref="IStore.List"/>.
/// The full body is fetched on demand via <see cref="IStore.Get"/>.
/// </summary>
public sealed record EntityDescriptor(
    string Type,
    string Id,
    string? Path = null);

public sealed record StoreHealth(bool Ok, string? Reason = null, string? Root = null);

/// <summary>
/// Storage backend the API/UI talks to. OAE-2 ships only <see cref="StubStore"/>;
/// the real filesystem-backed implementation lands in OAE-3.
/// </summary>
public interface IStore
{
    StoreHealth HealthCheck();

    IReadOnlyList<EntityDescriptor> List(string entityType);

    /// <summary>
    /// Read the entity body as raw JSON. The store decides on-disk formatting;
    /// callers don't deserialise here.
    /// </summary>
    string Get(string entityType, string id);

    string Create(string entityType, string id, string json);
    string Update(string entityType, string id, string json);
    void Delete(string entityType, string id);
}

public sealed class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string entityType, string id)
        : base($"entity not found: {entityType}/{id}") { }
}

public sealed class StoreUnmountedException : Exception
{
    public StoreUnmountedException(string reason) : base(reason) { }
}
