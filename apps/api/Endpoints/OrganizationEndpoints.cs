using DealerOS.Api.Infrastructure;
using DealerOS.Modules.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace DealerOS.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/branches", async (HttpContext http, DealerOsDbContext db, CancellationToken ct) =>
        {
            var actor = ActorContextFactory.Create(http.User);
            return Results.Ok(await db.Branches.AsNoTracking()
                .Where(x => x.OrganizationId == actor.OrganizationId && actor.BranchIds.Contains(x.Id))
                .OrderBy(x => x.Name).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct));
        }).RequireAuthorization(Permissions.VehiclesRead).WithName("ListAccessibleBranches");
        return endpoints;
    }
}
