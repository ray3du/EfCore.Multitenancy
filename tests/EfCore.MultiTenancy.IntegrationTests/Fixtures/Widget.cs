namespace EfCore.MultiTenancy.IntegrationTests.Fixtures;

/// <summary>A trivial tenant-scoped entity used only to prove schema isolation.</summary>
public class Widget
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}
