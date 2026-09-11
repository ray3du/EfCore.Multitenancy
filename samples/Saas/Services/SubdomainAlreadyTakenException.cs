namespace Saas.Services;

/// <summary>Thrown when a company signup requests a subdomain that's already registered.</summary>
public class SubdomainAlreadyTakenException : Exception
{
    public SubdomainAlreadyTakenException(string subdomain)
        : base($"The subdomain '{subdomain}' is already taken.")
    {
    }
}
