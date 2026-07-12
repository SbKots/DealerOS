using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DealerOS.Modules.IdentityAccess;
using Microsoft.IdentityModel.Tokens;

namespace DealerOS.Api.Auth;

public sealed class JwtTokenService(IConfiguration configuration, TimeProvider timeProvider)
{
    public string Create(UserAccount user)
    {
        var claims = new List<Claim>
        {
            new("user_id", user.Id.ToString()),
            new("name", user.DisplayName),
            new("email", user.Email),
            new("org_id", user.OrganizationId.ToString())
        };
        claims.AddRange(user.BranchAccess.Select(x => new Claim("branch_id", x.BranchId.ToString())));
        claims.AddRange(user.PermissionSet.Select(x => new Claim("permission", x)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.")));
        var token = new JwtSecurityToken(
            configuration["Jwt:Issuer"], configuration["Jwt:Audience"], claims,
            notBefore: timeProvider.GetUtcNow().UtcDateTime,
            expires: timeProvider.GetUtcNow().AddHours(8).UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
