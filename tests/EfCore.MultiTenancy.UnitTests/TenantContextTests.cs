using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Exceptions;
using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.UnitTests;

/// <summary>
/// TenantContext is the single source of truth for "who is the current tenant" within
/// one DI scope. These tests pin down its set-once semantics, since a context that
/// could be silently reassigned mid-scope is exactly the kind of bug that would let
/// one request's work bleed into another tenant.
/// </summary>
public class TenantContextTests
{
    private static Tenant MakeTenant(string schema = "acme") =>
        new() { Name = "Acme", SchemaName = schema };

    [Fact]
    public void HasTenant_IsFalse_BeforeAnyTenantIsSet()
    {
        var context = new TenantContext<Tenant>();

        Assert.False(context.HasTenant);
        Assert.Null(context.Current);
    }

    [Fact]
    public void Require_Throws_BeforeAnyTenantIsSet()
    {
        var context = new TenantContext<Tenant>();

        Assert.Throws<TenantContextException>(() => context.Require());
    }

    [Fact]
    public void SetTenant_MakesTenantAvailableViaCurrentAndRequire()
    {
        var context = new TenantContext<Tenant>();
        var tenant = MakeTenant();

        ((ITenantContextSetter<Tenant>)context).SetTenant(tenant);

        Assert.True(context.HasTenant);
        Assert.Same(tenant, context.Current);
        Assert.Same(tenant, context.Require());
    }

    [Fact]
    public void SetTenant_CalledTwice_ThrowsAndDoesNotReplaceTheFirstTenant()
    {
        var context = new TenantContext<Tenant>();
        var setter = (ITenantContextSetter<Tenant>)context;
        var first = MakeTenant("first_tenant");
        var second = MakeTenant("second_tenant");

        setter.SetTenant(first);

        Assert.Throws<TenantAlreadySetException>(() => setter.SetTenant(second));
        // The whole point of set-once semantics: a rejected second call must not
        // have mutated state, so the scope stays bound to the original tenant.
        Assert.Same(first, context.Current);
    }

    [Fact]
    public void SetTenant_NullTenant_Throws()
    {
        var context = new TenantContext<Tenant>();
        var setter = (ITenantContextSetter<Tenant>)context;

        Assert.Throws<ArgumentNullException>(() => setter.SetTenant(null!));
    }

    [Fact]
    public void ITenantContext_And_ITenantContextSetter_ShareState_WhenBackedByTheSameInstance()
    {
        // AddMultiTenancy registers both interfaces resolving to the same scoped
        // TenantContext<T> instance — this reproduces that wiring directly to prove
        // a value set through the setter is visible through the reader.
        var shared = new TenantContext<Tenant>();
        ITenantContext<Tenant> reader = shared;
        ITenantContextSetter<Tenant> writer = shared;
        var tenant = MakeTenant();

        writer.SetTenant(tenant);

        Assert.Same(tenant, reader.Current);
    }
}
