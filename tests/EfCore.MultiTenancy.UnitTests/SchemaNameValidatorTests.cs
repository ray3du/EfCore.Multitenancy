using EfCore.MultiTenancy.Core.Exceptions;
using EfCore.MultiTenancy.Core.Validation;

namespace EfCore.MultiTenancy.UnitTests;

/// <summary>
/// SchemaNameValidator is the sole guard between user-controlled input (a tenant's
/// chosen name) and raw SQL (CREATE SCHEMA, SET search_path). These tests exist to
/// make sure that guard actually rejects what it claims to reject.
/// </summary>
public class SchemaNameValidatorTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme_corp")]
    [InlineData("_leading_underscore")]
    [InlineData("a1b2c3")]
    public void Validate_AcceptsWellFormedNames(string schemaName)
    {
        SchemaNameValidator.Validate(schemaName);
        Assert.True(SchemaNameValidator.IsValid(schemaName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsNullOrWhitespace(string? schemaName)
    {
        Assert.Throws<InvalidSchemaNameException>(() => SchemaNameValidator.Validate(schemaName));
    }

    [Theory]
    [InlineData("Acme")] // uppercase not allowed
    [InlineData("1acme")] // must not start with a digit
    [InlineData("acme-corp")] // hyphen not allowed
    [InlineData("acme corp")] // space not allowed
    [InlineData("acme;DROP TABLE tenants;--")] // classic injection payload
    [InlineData("acme\"; SET search_path TO other; --")]
    [InlineData("acme.public")] // dot not allowed (would escape identifier quoting)
    public void Validate_RejectsMalformedOrInjectionLikeNames(string schemaName)
    {
        Assert.Throws<InvalidSchemaNameException>(() => SchemaNameValidator.Validate(schemaName));
        Assert.False(SchemaNameValidator.IsValid(schemaName));
    }

    [Theory]
    [InlineData("public")]
    [InlineData("pg_catalog")]
    [InlineData("pg_toast")]
    [InlineData("information_schema")]
    [InlineData("pg_anything_custom")]
    public void Validate_RejectsReservedSchemaNames(string schemaName)
    {
        Assert.Throws<InvalidSchemaNameException>(() => SchemaNameValidator.Validate(schemaName));
    }

    [Fact]
    public void Validate_RejectsNamesLongerThanPostgresIdentifierLimit()
    {
        var tooLong = new string('a', 64);
        Assert.Throws<InvalidSchemaNameException>(() => SchemaNameValidator.Validate(tooLong));
    }

    [Fact]
    public void Validate_AcceptsNameAtExactlyThePostgresIdentifierLimit()
    {
        var exactly63 = new string('a', 63);
        SchemaNameValidator.Validate(exactly63);
    }

    [Theory]
    [InlineData("Acme Corp", "acme_corp")]
    [InlineData("  Globex, Inc.  ", "globex_inc")]
    [InlineData("123 Startup", "t_123_startup")]
    [InlineData("Café", "caf")]
    public void Normalize_ProducesValidSchemaNames(string input, string expected)
    {
        var normalized = SchemaNameValidator.Normalize(input);

        Assert.Equal(expected, normalized);
        SchemaNameValidator.Validate(normalized); // must always round-trip through Validate cleanly
    }

    [Fact]
    public void Normalize_ThrowsWhenInputHasNoUsableCharacters()
    {
        Assert.Throws<InvalidSchemaNameException>(() => SchemaNameValidator.Normalize("!!!"));
    }

    [Fact]
    public void Normalize_TruncatesToPostgresIdentifierLimit()
    {
        var longName = new string('a', 100);

        var normalized = SchemaNameValidator.Normalize(longName);

        Assert.True(normalized.Length <= 63);
        SchemaNameValidator.Validate(normalized);
    }
}
