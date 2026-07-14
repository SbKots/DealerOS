using DealerOS.Api.Infrastructure;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Reservations.Application;

namespace DealerOS.Api.Endpoints;

public static class ReservationEndpoints
{
    public static IEndpointRouteBuilder MapReservationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reservations").RequireAuthorization();
        group.MapGet("/", async (HttpContext http, ReservationService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ActorContextFactory.Create(http.User), ct)))
            .RequireAuthorization(Permissions.ReservationsView).WithName("ListReservations");
        group.MapGet("/{reservationId:guid}", async (Guid reservationId, HttpContext http,
            ReservationService service, CancellationToken ct) => Results.Ok(await service.GetAsync(
            ActorContextFactory.Create(http.User), reservationId, ct)))
            .RequireAuthorization(Permissions.ReservationsView).WithName("GetReservation");
        group.MapPost("/", async (CreateReservationRequest request, HttpContext http,
            ReservationService service, CancellationToken ct) => Results.Ok(await service.CreateAsync(
            ActorContextFactory.Create(http.User), request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.ReservationsCreate).WithName("CreateReservation");
        group.MapPost("/{reservationId:guid}/extend", async (Guid reservationId, ExtendReservationRequest request,
            HttpContext http, ReservationService service, CancellationToken ct) => Results.Ok(
            await service.ExtendAsync(ActorContextFactory.Create(http.User), reservationId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ReservationsExtend)
            .WithName("ExtendReservation");
        group.MapPost("/{reservationId:guid}/deposit", async (Guid reservationId,
            RegisterReservationDepositRequest request, HttpContext http, ReservationService service,
            CancellationToken ct) => Results.Ok(await service.RegisterDepositAsync(
            ActorContextFactory.Create(http.User), reservationId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.ReservationsDeposit).WithName("RegisterReservationDeposit");
        group.MapPost("/{reservationId:guid}/cancel", async (Guid reservationId,
            ReservationReasonCommandRequest request, HttpContext http, ReservationService service,
            CancellationToken ct) => Results.Ok(await service.CancelAsync(ActorContextFactory.Create(http.User),
            reservationId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.ReservationsCancel).WithName("CancelReservation");
        group.MapPost("/{reservationId:guid}/expire", async (Guid reservationId,
            ReservationCommandRequest request, HttpContext http, ReservationService service,
            CancellationToken ct) => Results.Ok(await service.ExpireAsync(ActorContextFactory.Create(http.User),
            reservationId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.ReservationsEdit).WithName("ExpireReservation");
        return endpoints;
    }
}
