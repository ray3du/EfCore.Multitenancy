using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.Core.Validation;
using EfCore.MultiTenancy.EfCore.Options;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EfCore.MultiTenancy.EfCore.Provisioning;

public sealed class DefaultTenantConnectionStringProvider<TTenant> : ITenantConnectionStringProvider<TTenant>
    where TTenant : class, ITenant
{
    private readonly PostgresMultiTenancyOptions _options;

    public DefaultTenantConnectionStringProvider(IOptions<PostgresMultiTenancyOptions> options)
    {
        _options = options.Value;
    }

    public string GetConnectionString(TTenant tenant)
    {
        if (_options.Mode == TenantIsolationMode.SchemaPerTenant)
        {
            // Every tenant shares the same connection string (and therefore the same
            // Npgsql pool); the interceptor's SET search_path is what isolates them.
            return _options.ConnectionString;
        }

        SchemaNameValidator.Validate(tenant.SchemaName);
        var databaseName = string.Format(_options.DatabaseNameTemplate, tenant.SchemaName);

        var builder = new NpgsqlConnectionStringBuilder(_options.ConnectionString)
        {
            Database = databaseName,
        };
        return builder.ConnectionString;
    }
}
