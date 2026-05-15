using OAE.Core.Store;

namespace OAE.Tests;

public class StoreTests
{
    [Fact]
    public void StubStore_health_is_not_ok()
    {
        var s = new StubStore("test reason");
        var h = s.HealthCheck();
        Assert.False(h.Ok);
        Assert.Equal("test reason", h.Reason);
    }

    [Fact]
    public void StubStore_list_is_empty_and_writes_throw()
    {
        var s = new StubStore();
        Assert.Empty(s.List("enemy"));
        Assert.Throws<EntityNotFoundException>(() => s.Get("enemy", "x"));
        Assert.Throws<StoreUnmountedException>(() => s.Create("enemy", "x", "{}"));
        Assert.Throws<StoreUnmountedException>(() => s.Update("enemy", "x", "{}"));
        Assert.Throws<StoreUnmountedException>(() => s.Delete("enemy", "x"));
    }

    [Fact]
    public void HotSwapStore_swap_replaces_inner_and_returns_previous()
    {
        var first = new StubStore("first");
        var second = new StubStore("second");
        var swap = new HotSwapStore(first);

        Assert.Same(first, swap.Inner);
        var prev = swap.Swap(second);
        Assert.Same(first, prev);
        Assert.Same(second, swap.Inner);
        Assert.Equal("second", swap.HealthCheck().Reason);
    }
}
