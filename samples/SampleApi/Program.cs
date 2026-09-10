using EfCore.MultiTenancy.AspNetCore.DependencyInjection;
using EfCore.MultiTenancy.Core.Exceptions;
using SampleApi.Data;
using SampleApi.Models;
using SampleApi.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
    .AddMultiTenancy<AppTenant>(
        db =>
        {
            db.ConnectionString = builder.Configuration.GetConnectionString("Default")
                ?? "Host=localhost;Database=multitenancy_sample;Username=postgres;Password=postgres";
            // TenantStoreDbContext<AppTenant> is defined in the library's assembly;
            // this redirects its migrations into SampleApi so `dotnet ef migrations
            // add --context TenantStoreDbContext`1` generates files here.
            db.StoreMigrationsAssembly = typeof(Program).Assembly.GetName().Name;
        },
        resolution =>
        {
            // e.g. "acme.localhost" maps to tenant subdomain "acme" when BaseDomain is "localhost".
            resolution.BaseDomain = builder.Configuration["MultiTenancy:BaseDomain"] ?? "localhost";
            // Admin endpoints (tenant creation/listing) must work with no tenant resolved,
            // so this sample resolves tenants opportunistically and lets TodosController's
            // dependency on the tenant-scoped DbContext fail fast (caught below) instead.
            resolution.RequireResolvedTenant = false;
        })
    .AddTenantDbContext<AppTenant, SampleTenantDbContext>()
    .UseSubdomainResolution()
    .UseHeaderResolution()
    .AddTenantLifecycleHandler<AppTenant, SeedTodosOnTenantCreated>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();

// Translates "this endpoint needed a tenant but none was resolved" into a clean 400
// instead of an unhandled-exception 500. Placed before tenant resolution so it wraps
// everything downstream, including exceptions thrown while constructing
// SampleTenantDbContext (which calls ITenantContext<AppTenant>.Require()).
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (TenantContextException)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync(
            "This endpoint requires a resolved tenant. Use a tenant subdomain " +
            "(e.g. acme.localhost) or the X-Tenant header.");
    }
});

app.UseTenantResolution<AppTenant>();

app.UseAuthorization();

app.MapControllers();

app.Run();
