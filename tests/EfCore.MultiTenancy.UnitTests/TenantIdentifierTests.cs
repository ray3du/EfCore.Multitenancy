using EfCore.MultiTenancy.EfCore.Administration;

namespace EfCore.MultiTenancy.UnitTests;

public class TenantIdentifierTests
{
    [Fact]
    public void ForId_SetsOnlyId()
    {
        var id = Guid.NewGuid();
        var identifier = TenantIdentifier.ForId(id);

        Assert.Equal(id, identifier.Id);
        Assert.Null(identifier.SchemaName);
        Assert.Null(identifier.Domain);
        Assert.Equal($"id '{id}'", identifier.ToString());
    }

    [Fact]
    public void ForSchemaName_SetsOnlySchemaName()
    {
        var identifier = TenantIdentifier.ForSchemaName("acme");

        Assert.Null(identifier.Id);
        Assert.Equal("acme", identifier.SchemaName);
        Assert.Null(identifier.Domain);
        Assert.Equal("schema 'acme'", identifier.ToString());
    }

    [Fact]
    public void ForDomain_SetsOnlyDomain()
    {
        var identifier = TenantIdentifier.ForDomain("acme.example.com");

        Assert.Null(identifier.Id);
        Assert.Null(identifier.SchemaName);
        Assert.Equal("acme.example.com", identifier.Domain);
        Assert.Equal("domain 'acme.example.com'", identifier.ToString());
    }

    [Fact]
    public void TwoIdentifiersForTheSameField_AreEqual()
    {
        var id = Guid.NewGuid();

        Assert.Equal(TenantIdentifier.ForId(id), TenantIdentifier.ForId(id));
        Assert.Equal(TenantIdentifier.ForSchemaName("acme"), TenantIdentifier.ForSchemaName("acme"));
    }
}
