using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Saas.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetActorId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("Token has no subject claim.");
        return Guid.Parse(value);
    }

    public static bool IsStaff(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(AuthClaims.ActorType) == AuthClaims.ActorTypeStaff;
}
