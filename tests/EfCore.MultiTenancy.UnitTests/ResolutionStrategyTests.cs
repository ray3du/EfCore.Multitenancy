using EfCore.MultiTenancy.AspNetCore.Options;
using EfCore.MultiTenancy.AspNetCore.Resolution;
using EfCore.MultiTenancy.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EfCore.MultiTenancy.UnitTests;

public class SubdomainTenantResolutionStrategyTests
{
    private static SubdomainTenantResolutionStrategy<Tenant> MakeStrategy(
        string? baseDomain = "myapp.com", params string[] excluded)
    {
        var options = new TenantResolutionOptions { BaseDomain = baseDomain };
        if (excluded.Length > 0)
        {
            options.ExcludedSubdomains = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        }

        return new SubdomainTenantResolutionStrategy<Tenant>(Options.Create(options));
    }

    private static HttpContext MakeContext(string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        return context;
    }

    [Fact]
    public async Task TryResolveAsync_ExtractsSubdomain_ForMatchingHost()
    {
        var strategy = MakeStrategy();

        var result = await strategy.TryResolveAsync(MakeContext("acme.myapp.com"));

        Assert.NotNull(result);
        Assert.Equal("acme", result!.Value.Domain);
        Assert.Null(result.Value.TenantId);
    }

    [Fact]
    public async Task TryResolveAsync_ReturnsNull_ForBareBaseDomain()
    {
        var strategy = MakeStrategy();

        var result = await strategy.TryResolveAsync(MakeContext("myapp.com"));

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_ReturnsNull_ForUnrelatedHost()
    {
        var strategy = MakeStrategy();

        var result = await strategy.TryResolveAsync(MakeContext("evil.com"));

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_ReturnsNull_ForExcludedSubdomain()
    {
        var strategy = MakeStrategy(excluded: "www");

        var result = await strategy.TryResolveAsync(MakeContext("www.myapp.com"));

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_ReturnsNull_WhenBaseDomainNotConfigured()
    {
        var strategy = MakeStrategy(baseDomain: null);

        var result = await strategy.TryResolveAsync(MakeContext("acme.myapp.com"));

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_ReturnsNull_ForNestedSubdomain()
    {
        // "a.b.myapp.com" is not a single clean tenant subdomain segment.
        var strategy = MakeStrategy();

        var result = await strategy.TryResolveAsync(MakeContext("a.b.myapp.com"));

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_LowercasesTheExtractedSubdomain()
    {
        var strategy = MakeStrategy();

        var result = await strategy.TryResolveAsync(MakeContext("ACME.myapp.com"));

        Assert.Equal("acme", result!.Value.Domain);
    }
}

public class HeaderTenantResolutionStrategyTests
{
    private static HeaderTenantResolutionStrategy<Tenant> MakeStrategy(string headerName = "X-Tenant") =>
        new(Options.Create(new TenantResolutionOptions { TenantHeaderName = headerName }));

    [Fact]
    public async Task TryResolveAsync_ExtractsHeaderValue()
    {
        var strategy = MakeStrategy();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant"] = "acme";

        var result = await strategy.TryResolveAsync(context);

        Assert.NotNull(result);
        Assert.Equal("acme", result!.Value.Domain);
    }

    [Fact]
    public async Task TryResolveAsync_ReturnsNull_WhenHeaderMissing()
    {
        var strategy = MakeStrategy();
        var context = new DefaultHttpContext();

        var result = await strategy.TryResolveAsync(context);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_ReturnsNull_WhenHeaderIsWhitespace()
    {
        var strategy = MakeStrategy();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant"] = "   ";

        var result = await strategy.TryResolveAsync(context);

        Assert.Null(result);
    }
}

public class ClaimTenantResolutionStrategyTests
{
    private static ClaimTenantResolutionStrategy<Tenant> MakeStrategy(string claimType = "tenant_id") =>
        new(Options.Create(new TenantResolutionOptions { TenantClaimType = claimType }));

    [Fact]
    public async Task TryResolveAsync_ExtractsTenantIdFromClaim()
    {
        var strategy = MakeStrategy();
        var tenantId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim("tenant_id", tenantId.ToString()) }));

        var result = await strategy.TryResolveAsync(context);

        Assert.NotNull(result);
        Assert.Equal(tenantId, result!.Value.TenantId);
        Assert.Null(result.Value.Domain);
    }

    [Fact]
    public async Task TryResolveAsync_ReturnsNull_WhenClaimMissing()
    {
        var strategy = MakeStrategy();
        var context = new DefaultHttpContext();

        var result = await strategy.TryResolveAsync(context);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_ReturnsNull_WhenClaimIsNotAGuid()
    {
        var strategy = MakeStrategy();
        var context = new DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim("tenant_id", "not-a-guid") }));

        var result = await strategy.TryResolveAsync(context);

        Assert.Null(result);
    }
}
