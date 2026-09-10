using EfCore.MultiTenancy.Core.Events;
using EfCore.MultiTenancy.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfCore.MultiTenancy.UnitTests;

public class TenantLifecycleDispatcherTests
{
    private class RecordingHandler : ITenantLifecycleHandler<Tenant>
    {
        public List<string> Events { get; } = new();

        public Task OnTenantCreatedAsync(Tenant tenant, CancellationToken cancellationToken = default)
        {
            Events.Add("created");
            return Task.CompletedTask;
        }

        public Task OnTenantMigratedAsync(Tenant tenant, CancellationToken cancellationToken = default)
        {
            Events.Add("migrated");
            return Task.CompletedTask;
        }

        public Task OnTenantResolvedAsync(Tenant tenant, CancellationToken cancellationToken = default)
        {
            Events.Add("resolved");
            return Task.CompletedTask;
        }
    }

    private class ThrowingHandler : ITenantLifecycleHandler<Tenant>
    {
        public Task OnTenantCreatedAsync(Tenant tenant, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }

    private static Tenant MakeTenant() => new() { Name = "Acme", SchemaName = "acme" };

    [Fact]
    public async Task RaiseTenantCreatedAsync_InvokesAllRegisteredHandlers()
    {
        var handler1 = new RecordingHandler();
        var handler2 = new RecordingHandler();
        var dispatcher = new TenantLifecycleDispatcher<Tenant>(
            new ITenantLifecycleHandler<Tenant>[] { handler1, handler2 },
            NullLogger<TenantLifecycleDispatcher<Tenant>>.Instance);

        await dispatcher.RaiseTenantCreatedAsync(MakeTenant());

        Assert.Equal(new[] { "created" }, handler1.Events);
        Assert.Equal(new[] { "created" }, handler2.Events);
    }

    [Fact]
    public async Task RaiseTenantCreatedAsync_OneHandlerThrowing_DoesNotPreventOthersFromRunning()
    {
        // A misbehaving seed handler must not be able to break tenant creation for
        // every other handler (or, when raised from resolution, the request itself).
        var throwing = new ThrowingHandler();
        var recording = new RecordingHandler();
        var dispatcher = new TenantLifecycleDispatcher<Tenant>(
            new ITenantLifecycleHandler<Tenant>[] { throwing, recording },
            NullLogger<TenantLifecycleDispatcher<Tenant>>.Instance);

        await dispatcher.RaiseTenantCreatedAsync(MakeTenant());

        Assert.Equal(new[] { "created" }, recording.Events);
    }

    [Fact]
    public async Task RaiseTenantCreatedAsync_WithNoHandlers_CompletesWithoutError()
    {
        var dispatcher = new TenantLifecycleDispatcher<Tenant>(
            Array.Empty<ITenantLifecycleHandler<Tenant>>(),
            NullLogger<TenantLifecycleDispatcher<Tenant>>.Instance);

        await dispatcher.RaiseTenantCreatedAsync(MakeTenant());
    }
}
