using DealerOS.Api.Infrastructure;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Sales.Application;

namespace DealerOS.Api.Endpoints;

public static class SalesEndpoints
{
    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var visits = endpoints.MapGroup("/api/sales/visits").RequireAuthorization();
        visits.MapGet("/", async (HttpContext http, SalesService service, CancellationToken ct) => Results.Ok(
            await service.ListVisitsAsync(ActorContextFactory.Create(http.User), ct)))
            .RequireAuthorization(Permissions.SalesVisitsView).WithName("ListSalesVisits");
        visits.MapGet("/{visitId:guid}", async (Guid visitId, HttpContext http, SalesService service,
            CancellationToken ct) => Results.Ok(await service.GetVisitAsync(ActorContextFactory.Create(http.User),
            visitId, ct))).RequireAuthorization(Permissions.SalesVisitsView).WithName("GetSalesVisit");
        visits.MapPost("/", async (CreateVisitRequest request, HttpContext http, SalesService service,
            CancellationToken ct) => Results.Ok(await service.CreateVisitAsync(ActorContextFactory.Create(http.User),
            request, http.TraceIdentifier, ct))).RequireAuthorization(Permissions.SalesVisitsCreate)
            .WithName("CreateSalesVisit");
        visits.MapPost("/{visitId:guid}/reschedule", async (Guid visitId, RescheduleVisitRequest request,
            HttpContext http, SalesService service, CancellationToken ct) => Results.Ok(await service.RescheduleAsync(
                ActorContextFactory.Create(http.User), visitId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.SalesVisitsEdit).WithName("RescheduleSalesVisit");
        visits.MapPost("/{visitId:guid}/arrive", async (Guid visitId, VisitCommandRequest request,
            HttpContext http, SalesService service, CancellationToken ct) => Results.Ok(await service.ArriveAsync(
                ActorContextFactory.Create(http.User), visitId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.SalesVisitsEdit).WithName("ArriveSalesVisit");
        visits.MapPost("/{visitId:guid}/test-drive/check-out", async (Guid visitId,
            TestDriveCheckOutRequest request, HttpContext http, SalesService service, CancellationToken ct) =>
            Results.Ok(await service.CheckOutAsync(ActorContextFactory.Create(http.User), visitId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.SalesVisitsEdit)
            .WithName("CheckOutTestDrive");
        visits.MapPost("/{visitId:guid}/test-drive/check-in", async (Guid visitId,
            TestDriveCheckInRequest request, HttpContext http, SalesService service, CancellationToken ct) =>
            Results.Ok(await service.CheckInAsync(ActorContextFactory.Create(http.User), visitId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.SalesVisitsEdit)
            .WithName("CheckInTestDrive");
        visits.MapPost("/{visitId:guid}/complete", async (Guid visitId, CompleteVisitRequest request,
            HttpContext http, SalesService service, CancellationToken ct) => Results.Ok(
                await service.CompleteVisitAsync(ActorContextFactory.Create(http.User), visitId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.SalesVisitsComplete)
            .WithName("CompleteSalesVisit");
        visits.MapPost("/{visitId:guid}/no-show", async (Guid visitId, VisitReasonCommandRequest request,
            HttpContext http, SalesService service, CancellationToken ct) => Results.Ok(await service.NoShowAsync(
                ActorContextFactory.Create(http.User), visitId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.SalesVisitsComplete).WithName("NoShowSalesVisit");
        visits.MapPost("/{visitId:guid}/cancel", async (Guid visitId, VisitReasonCommandRequest request,
            HttpContext http, SalesService service, CancellationToken ct) => Results.Ok(
                await service.CancelVisitAsync(ActorContextFactory.Create(http.User), visitId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.SalesVisitsEdit)
            .WithName("CancelSalesVisit");

        var offers = endpoints.MapGroup("/api/sales/offers").RequireAuthorization();
        offers.MapGet("/", async (HttpContext http, SalesService service, CancellationToken ct) => Results.Ok(
            await service.ListOffersAsync(ActorContextFactory.Create(http.User), ct)))
            .RequireAuthorization(Permissions.SalesOffersView).WithName("ListSalesOffers");
        offers.MapGet("/{offerId:guid}", async (Guid offerId, HttpContext http, SalesService service,
            CancellationToken ct) => Results.Ok(await service.GetOfferAsync(ActorContextFactory.Create(http.User),
            offerId, ct))).RequireAuthorization(Permissions.SalesOffersView).WithName("GetSalesOffer");
        offers.MapPost("/preview", async (PreviewOfferRequest request, HttpContext http, SalesService service,
            CancellationToken ct) => Results.Ok(await service.PreviewOfferAsync(
                ActorContextFactory.Create(http.User), request, ct)))
            .RequireAuthorization(Permissions.SalesOffersCreate).WithName("PreviewSalesOffer");
        offers.MapPost("/", async (CreateOfferRequest request, HttpContext http, SalesService service,
            CancellationToken ct) => Results.Ok(await service.CreateOfferAsync(ActorContextFactory.Create(http.User),
            request, http.TraceIdentifier, ct))).RequireAuthorization(Permissions.SalesOffersCreate)
            .WithName("CreateSalesOffer");
        offers.MapPut("/{offerId:guid}", async (Guid offerId, UpdateOfferRequest request, HttpContext http,
            SalesService service, CancellationToken ct) => Results.Ok(await service.UpdateOfferAsync(
                ActorContextFactory.Create(http.User), offerId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.SalesOffersEdit).WithName("UpdateSalesOffer");
        offers.MapPost("/{offerId:guid}/submit", async (Guid offerId, SubmitOfferRequest request,
            HttpContext http, SalesService service, CancellationToken ct) => Results.Ok(await service.SubmitOfferAsync(
                ActorContextFactory.Create(http.User), offerId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.SalesOffersSubmit).WithName("SubmitSalesOffer");
        offers.MapPost("/{offerId:guid}/decision", async (Guid offerId, DecideOfferRequest request,
            HttpContext http, SalesService service, CancellationToken ct) => Results.Ok(await service.DecideOfferAsync(
                ActorContextFactory.Create(http.User), offerId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.SalesOffersApprove).WithName("DecideSalesOffer");
        offers.MapPost("/{offerId:guid}/revisions", async (Guid offerId,
            CreateOfferRevisionRequest request, HttpContext http, SalesService service, CancellationToken ct) =>
            Results.Ok(await service.CreateRevisionAsync(ActorContextFactory.Create(http.User), offerId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.SalesOffersEdit)
            .WithName("CreateSalesOfferRevision");
        offers.MapPost("/{offerId:guid}/cancel", async (Guid offerId, OfferReasonCommandRequest request,
            HttpContext http, SalesService service, CancellationToken ct) => Results.Ok(await service.CancelOfferAsync(
                ActorContextFactory.Create(http.User), offerId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.SalesOffersCancel).WithName("CancelSalesOffer");
        offers.MapPost("/{offerId:guid}/expire", async (Guid offerId, SubmitOfferRequest request,
            HttpContext http, SalesService service, CancellationToken ct) => Results.Ok(await service.ExpireOfferAsync(
                ActorContextFactory.Create(http.User), offerId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.SalesOffersEdit).WithName("ExpireSalesOffer");
        return endpoints;
    }
}
