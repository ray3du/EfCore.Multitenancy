namespace Saas.Auth;

/// <summary>Claim types and policy names shared between token issuance and authorization setup.</summary>
public static class AuthClaims
{
    public const string ActorType = "actor_type";
    public const string ActorTypeStaff = "staff";
    public const string ActorTypeCustomer = "customer";

    public const string Role = "role";

    public const string StaffOnlyPolicy = "StaffOnly";
    public const string AdminOnlyPolicy = "AdminOnly";
    public const string CustomerOnlyPolicy = "CustomerOnly";
}
