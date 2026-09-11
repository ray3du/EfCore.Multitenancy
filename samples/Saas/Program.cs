using System.Text;
using System.Text.Json.Serialization;
using EfCore.MultiTenancy.AspNetCore.DependencyInjection;
using EfCore.MultiTenancy.Core.Exceptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Saas.Auth;
using Saas.Data;
using Saas.Models;
using Saas.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Order <-> Invoice is a navigation cycle (Order.Invoice, Invoice.Orders), and
        // controllers here return entities directly rather than DTOs.
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        // Enums (UserRole, OrderStatus, InvoiceStatus) as "Admin"/"Pending" rather
        // than raw numbers, for both requests and responses.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");

builder.Services
    .AddMultiTenancy<Company>(
        db =>
        {
            db.ConnectionString = builder.Configuration.GetConnectionString("Default")
                ?? "Host=localhost;Database=saas_sample;Username=postgres;Password=postgres";
            db.StoreMigrationsAssembly = typeof(Program).Assembly.GetName().Name;
        },
        resolution =>
        {
            resolution.BaseDomain = builder.Configuration["MultiTenancy:BaseDomain"] ?? "localhost";
            // Company signup runs on the bare base domain, with no tenant resolved yet.
            resolution.RequireResolvedTenant = false;
        })
    .AddTenantDbContext<Company, SaasTenantDbContext>()
    .UseSubdomainResolution();

builder.Services.AddScoped<TenantOnboardingService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddSingleton<JwtTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, the handler remaps short claim types like "sub" and "role"
        // to long legacy XML-namespaced ClaimTypes (e.g. ClaimTypes.NameIdentifier)
        // on the way in, which would silently break every FindFirstValue("sub")/
        // RequireClaim("role", ...) lookup in this app that expects the claim types
        // exactly as JwtTokenService issued them.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthClaims.StaffOnlyPolicy, policy => policy.RequireClaim(AuthClaims.ActorType, AuthClaims.ActorTypeStaff))
    .AddPolicy(AuthClaims.AdminOnlyPolicy, policy => policy
        .RequireClaim(AuthClaims.ActorType, AuthClaims.ActorTypeStaff)
        .RequireClaim(AuthClaims.Role, nameof(UserRole.Admin)))
    .AddPolicy(AuthClaims.CustomerOnlyPolicy, policy => policy.RequireClaim(AuthClaims.ActorType, AuthClaims.ActorTypeCustomer));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();

// Translates "this endpoint needed a tenant but none was resolved" into a clean 400
// instead of an unhandled-exception 500 (e.g. hitting a tenant-scoped endpoint from
// the bare base domain).
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
            "This endpoint requires a resolved tenant. Use a company subdomain, e.g. acme.localhost.");
    }
});

app.UseTenantResolution<Company>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
