using System.Data.Common;
using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.Core.Validation;
using EfCore.MultiTenancy.EfCore.Options;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace EfCore.MultiTenancy.EfCore.Context;

/// <summary>
/// Sets PostgreSQL's <c>search_path</c> to the current tenant's schema every single
/// time a connection is opened for a <see cref="TenantDbContext{TTenant}"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the crux of tenant isolation in this library, so its invariants are
/// spelled out explicitly:
/// </para>
/// <list type="number">
/// <item>
/// It is registered <b>scoped</b> in DI (see <c>AddMultiTenancy</c>) and depends on
/// the scoped <see cref="ITenantContext{TTenant}"/>, so each request/scope gets its
/// own interceptor instance bound to that scope's resolved tenant — never a shared
/// or static instance.
/// </item>
/// <item>
/// It runs unconditionally on every <c>ConnectionOpened</c>/<c>ConnectionOpenedAsync</c>
/// callback, with no "skip if already set" shortcut. Physical connections come from
/// Npgsql's ADO.NET connection pool and Npgsql does not reset session state (such as
/// a previous tenant's search_path) when a connection returns to the pool. Relying on
/// "probably still correct from last time" would be the leak; always overwriting it
/// on open is what makes it safe to reuse pooled connections at all.
/// </item>
/// <item>
/// The schema name is written into a non-parameterizable <c>SET search_path</c>
/// statement (PostgreSQL does not support bind parameters for session GUCs), so it
/// is validated through <see cref="SchemaNameValidator"/> immediately before use —
/// defense in depth even though the value should already have been validated when
/// the tenant was created.
/// </item>
/// <item>
/// <c>public</c> is always appended after the tenant schema so unqualified references
/// to genuinely shared objects (e.g. extensions installed in public) still resolve,
/// while the tenant schema is searched first for everything else.
/// </item>
/// </list>
/// </remarks>
public sealed class TenantSchemaConnectionInterceptor<TTenant> : DbConnectionInterceptor
    where TTenant : class, ITenant
{
    private readonly ITenantContext<TTenant> _tenantContext;
    private readonly TenantIsolationMode _mode;

    public TenantSchemaConnectionInterceptor(
        ITenantContext<TTenant> tenantContext,
        IOptions<PostgresMultiTenancyOptions> options)
    {
        _tenantContext = tenantContext;
        _mode = options.Value.Mode;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (_mode == TenantIsolationMode.SchemaPerTenant)
        {
            ApplySearchPath(connection);
        }
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (_mode == TenantIsolationMode.SchemaPerTenant)
        {
            await ApplySearchPathAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private void ApplySearchPath(DbConnection connection)
    {
        var sql = BuildSetSearchPathSql();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private async Task ApplySearchPathAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var sql = BuildSetSearchPathSql();
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private string BuildSetSearchPathSql()
    {
        var tenant = _tenantContext.Require();
        SchemaNameValidator.Validate(tenant.SchemaName);

        // Safe to interpolate: SchemaNameValidator enforces ^[a-z_][a-z0-9_]*$, and
        // SET search_path cannot take a bind parameter for the schema identifier.
        return $"SET search_path TO \"{tenant.SchemaName}\", public";
    }
}
