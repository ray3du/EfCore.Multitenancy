using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Saas.Models;

namespace Saas.Auth;

/// <summary>
/// Issues JWTs for the two identity types in this sample. The token only encodes
/// *identity* (who, and staff vs. customer, plus role for staff) — the tenant itself
/// is resolved per-request from the subdomain, not from the token, so both staff and
/// customer tokens are only ever valid on the subdomain they were issued for.
/// </summary>
public class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string CreateStaffToken(User user)
    {
        return CreateToken(
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(AuthClaims.ActorType, AuthClaims.ActorTypeStaff),
            new Claim(AuthClaims.Role, user.Role.ToString()),
        ]);
    }

    public string CreateCustomerToken(Customer customer)
    {
        return CreateToken(
        [
            new Claim(JwtRegisteredClaimNames.Sub, customer.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, customer.Email),
            new Claim(ClaimTypes.Name, customer.Name),
            new Claim(AuthClaims.ActorType, AuthClaims.ActorTypeCustomer),
        ]);
    }

    private string CreateToken(IEnumerable<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
