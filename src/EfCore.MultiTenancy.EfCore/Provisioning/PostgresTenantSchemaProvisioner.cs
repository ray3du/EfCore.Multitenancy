using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.Core.Validation;
using EfCore.MultiTenancy.EfCore.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EfCore.MultiTenancy.EfCore.Provisioning;

/// <summary>
/// Default provisioner. Uses a raw <see cref="NpgsqlConnection"/> against the base
/// connection string — not a <c>TenantDbContext</c> — because in schema mode the
/// target schema does not exist yet, and DDL like <c>CREATE SCHEMA</c>/<c>CREATE DATABASE</c>
/// does not depend on search_path anyway.
/// </summary>
public sealed class PostgresTenantSchemaProvisioner<TTenant> : ITenantSchemaProvisioner<TTenant>
    where TTenant : class, ITenant
{
    private readonly PostgresMultiTenancyOptions _options;
    private readonly ILogger<PostgresTenantSchemaProvisioner<TTenant>> _logger;

    public PostgresTenantSchemaProvisioner(
        IOptions<PostgresMultiTenancyOptions> options,
        ILogger<PostgresTenantSchemaProvisioner<TTenant>> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task CreateAsync(TTenant tenant, CancellationToken cancellationToken = default)
    {
        SchemaNameValidator.Validate(tenant.SchemaName);

        if (_options.Mode == TenantIsolationMode.SchemaPerTenant)
        {
            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            // Identifier validated above; CREATE SCHEMA has no parameterized form.
            command.CommandText = $"CREATE SCHEMA IF NOT EXISTS \"{tenant.SchemaName}\"";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Created schema {SchemaName} for tenant {TenantId}", tenant.SchemaName, tenant.Id);
        }
        else
        {
            var databaseName = string.Format(_options.DatabaseNameTemplate, tenant.SchemaName);
            SchemaNameValidator.Validate(databaseName);

            // CREATE DATABASE cannot run inside a multi-statement transaction block
            // and must target the maintenance connection (not the new database).
            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateDatabase)
            {
                // Idempotent create, matching the "IF NOT EXISTS" semantics of schema mode.
            }

            _logger.LogInformation("Created database {DatabaseName} for tenant {TenantId}", databaseName, tenant.Id);
        }
    }

    public async Task DropAsync(TTenant tenant, CancellationToken cancellationToken = default)
    {
        SchemaNameValidator.Validate(tenant.SchemaName);

        if (_options.Mode == TenantIsolationMode.SchemaPerTenant)
        {
            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS \"{tenant.SchemaName}\" CASCADE";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var databaseName = string.Format(_options.DatabaseNameTemplate, tenant.SchemaName);
            SchemaNameValidator.Validate(databaseName);

            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
