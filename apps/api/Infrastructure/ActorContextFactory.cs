using System.Security.Claims;
using DealerOS.Modules.Vehicles.Application;

namespace DealerOS.Api.Infrastructure;

public static class ActorContextFactory
{
    public static ActorContext Create(ClaimsPrincipal principal)
    {
        var userId = Guid.Parse(principal.FindFirst("user_id")?.Value
            ?? throw new UnauthorizedAccessException("User ID claim is missing."));
        var organizationId = Guid.Parse(principal.FindFirst("org_id")?.Value
            ?? throw new UnauthorizedAccessException("Organization claim is missing."));
        var branches = principal.FindAll("branch_id").Select(x => Guid.Parse(x.Value)).ToHashSet();
        var permissions = principal.FindAll("permission").Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        return new ActorContext(userId, organizationId, branches, permissions);
    }
}
