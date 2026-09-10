namespace EfCore.MultiTenancy.Core.Exceptions;

/// <summary>Thrown when tenant resolution cannot find a matching, active tenant.</summary>
public class TenantNotFoundException : Exception
{
    public string? RequestedIdentifier { get; }

    public TenantNotFoundException(string? requestedIdentifier)
        : base($"No active tenant found for identifier '{requestedIdentifier}'.")
    {
        RequestedIdentifier = requestedIdentifier;
    }

    public TenantNotFoundException(string? requestedIdentifier, string message)
        : base(message)
    {
        RequestedIdentifier = requestedIdentifier;
    }
}
