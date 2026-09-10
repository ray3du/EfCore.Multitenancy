using System.Text.RegularExpressions;
using EfCore.MultiTenancy.Core.Exceptions;

namespace EfCore.MultiTenancy.Core.Validation;

/// <summary>
/// Validates PostgreSQL schema names before they are ever concatenated into raw SQL
/// (CREATE SCHEMA, SET search_path, DROP SCHEMA). This is the single most important
/// guard in the library: schema names routinely originate from user-controlled input
/// (a signup form, a tenant name) and Npgsql has no parameterized-identifier API for
/// DDL or SET commands, so every call site MUST validate through here first rather
/// than re-implementing its own check.
/// </summary>
public static partial class SchemaNameValidator
{
    // Lower-case letters, digits, underscore; must start with a letter or underscore.
    // This is intentionally stricter than PostgreSQL's own identifier rules (which allow
    // more characters when quoted) because we never want to rely on quoting alone to be safe.
    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex ValidPattern();

    private const int PostgresIdentifierMaxLength = 63;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "public",
        "pg_catalog",
        "pg_toast",
        "information_schema",
    };

    /// <summary>
    /// Throws <see cref="InvalidSchemaNameException"/> if <paramref name="schemaName"/>
    /// is not safe to interpolate into raw SQL as a schema identifier.
    /// </summary>
    public static void Validate(string? schemaName)
    {
        if (string.IsNullOrWhiteSpace(schemaName))
        {
            throw new InvalidSchemaNameException(schemaName ?? string.Empty, "Schema name cannot be null or empty.");
        }

        if (schemaName.Length > PostgresIdentifierMaxLength)
        {
            throw new InvalidSchemaNameException(
                schemaName,
                $"Schema name exceeds PostgreSQL's {PostgresIdentifierMaxLength}-character identifier limit.");
        }

        if (!ValidPattern().IsMatch(schemaName))
        {
            throw new InvalidSchemaNameException(
                schemaName,
                "Schema name must start with a lower-case letter or underscore and contain only " +
                "lower-case letters, digits, and underscores.");
        }

        if (ReservedNames.Contains(schemaName) || schemaName.StartsWith("pg_", StringComparison.Ordinal))
        {
            throw new InvalidSchemaNameException(schemaName, "Schema name is reserved by PostgreSQL.");
        }
    }

    /// <summary>Returns true if <paramref name="schemaName"/> would pass <see cref="Validate"/>.</summary>
    public static bool IsValid(string? schemaName)
    {
        try
        {
            Validate(schemaName);
            return true;
        }
        catch (InvalidSchemaNameException)
        {
            return false;
        }
    }

    /// <summary>
    /// Derives a valid, deterministic schema name from an arbitrary display string
    /// (e.g. a tenant's chosen name or subdomain). Consuming code should still store
    /// and reuse the resulting schema name rather than re-deriving it, since two
    /// different inputs can collide after normalization.
    /// </summary>
    public static string Normalize(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        var lowered = input.Trim().ToLowerInvariant();
        var builder = new System.Text.StringBuilder(lowered.Length + 1);

        foreach (var c in lowered)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        var normalized = builder.ToString().Trim('_');
        if (normalized.Length == 0)
        {
            throw new InvalidSchemaNameException(input, "Input normalizes to an empty schema name.");
        }

        if (!char.IsLetter(normalized[0]) && normalized[0] != '_')
        {
            normalized = "t_" + normalized;
        }

        if (normalized.Length > PostgresIdentifierMaxLength)
        {
            normalized = normalized[..PostgresIdentifierMaxLength].TrimEnd('_');
        }

        Validate(normalized);
        return normalized;
    }
}
