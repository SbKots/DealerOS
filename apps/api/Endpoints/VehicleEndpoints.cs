using DealerOS.Api.Infrastructure;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Vehicles.Application;

namespace DealerOS.Api.Endpoints;

public static class VehicleEndpoints
{
    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles").RequireAuthorization();

        group.MapGet("/", async (HttpContext http, VehicleIntakeService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ActorContextFactory.Create(http.User), ct)))
            .RequireAuthorization(Permissions.VehiclesRead).WithName("ListVehicles");

        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, VehicleIntakeService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(ActorContextFactory.Create(http.User), id, ct)))
            .RequireAuthorization(Permissions.VehiclesRead).WithName("GetVehicle");

        group.MapPost("/", async (CreateVehicleRequest request, HttpContext http, VehicleIntakeService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(ActorContextFactory.Create(http.User), request, http.TraceIdentifier, ct);
            return Results.Created($"/api/vehicles/{result.Id}", result);
        }).RequireAuthorization(Permissions.VehiclesCreate).WithName("CreateVehicle");

        group.MapPost("/{id:guid}/accept-to-stock", async (Guid id, HttpContext http, VehicleIntakeService service, CancellationToken ct) =>
            Results.Ok(await service.AcceptAsync(ActorContextFactory.Create(http.User), id, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.VehiclesAccept).WithName("AcceptVehicleToStock");

        return endpoints;
    }
}
