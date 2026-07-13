using System.Text.Json;
using DealerOS.Api.Infrastructure;
using DealerOS.Modules.IdentityAccess;
using DealerOS.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DealerOS.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", async (LoginRequest request, HttpContext http, DealerOsDbContext db,
            IPasswordHasher<UserAccount> hasher, JwtTokenService tokens, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                throw new DomainException("auth.credentials_required", "Email и пароль обязательны.");
            }
            if (request.Email.Length > 320 || request.Password.Length > 1024)
            {
                throw new DomainException("auth.credentials_too_long", "Email или пароль превышает допустимую длину.");
            }

            var email = request.Email.Trim().ToLowerInvariant();
            var user = await db.Users.Include(x => x.BranchAccess)
                .SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
            if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Неверный email или пароль.");
            }

            var organizationName = await db.Organizations.Where(x => x.Id == user.OrganizationId).Select(x => x.Name).SingleAsync(cancellationToken);
            var branchName = await db.Branches.Where(x => user.BranchAccess.Select(access => access.BranchId).Contains(x.Id))
                .OrderBy(x => x.Name).Select(x => x.Name).FirstAsync(cancellationToken);
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), user.OrganizationId, user.Id, "identity.login_succeeded",
                "UserAccount", user.Id, null, JsonSerializer.Serialize(new { user.IsActive }), http.TraceIdentifier,
                timeProvider.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new LoginResponse(tokens.Create(user), user.DisplayName, user.Email, organizationName, branchName));
        }).AllowAnonymous().RequireRateLimiting("login").WithName("Login");
        return endpoints;
    }
}

public sealed record LoginRequest(string? Email, string? Password);
public sealed record LoginResponse(string AccessToken, string DisplayName, string Email, string OrganizationName, string BranchName);
