using DealerOS.Api.Infrastructure;
using DealerOS.Modules.Deals.Application;
using DealerOS.Modules.IdentityAccess;

namespace DealerOS.Api.Endpoints;

public static class DealEndpoints
{
    public static IEndpointRouteBuilder MapDealEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/deals").RequireAuthorization();
        group.MapGet("/", async (HttpContext http, DealService service, CancellationToken ct) => Results.Ok(
            await service.ListAsync(ActorContextFactory.Create(http.User), ct)))
            .RequireAuthorization(Permissions.DealsView).WithName("ListDeals");
        group.MapGet("/{dealId:guid}", async (Guid dealId, HttpContext http, DealService service,
            CancellationToken ct) => Results.Ok(await service.GetAsync(ActorContextFactory.Create(http.User),
            dealId, ct))).RequireAuthorization(Permissions.DealsView).WithName("GetDeal");
        group.MapPost("/", async (CreateDealRequest request, HttpContext http, DealService service,
            CancellationToken ct) => Results.Ok(await service.CreateAsync(ActorContextFactory.Create(http.User),
            request, http.TraceIdentifier, ct))).RequireAuthorization(Permissions.DealsCreate).WithName("CreateDeal");
        group.MapPost("/{dealId:guid}/begin-payment", async (Guid dealId, DealCommandRequest request,
            HttpContext http, DealService service, CancellationToken ct) => Results.Ok(
            await service.BeginPaymentAsync(ActorContextFactory.Create(http.User), dealId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.DealsEdit).WithName("BeginDealPayment");
        group.MapPost("/{dealId:guid}/payments", async (Guid dealId, RegisterDealPaymentRequest request,
            HttpContext http, DealService service, CancellationToken ct) => Results.Ok(
            await service.RegisterPaymentAsync(ActorContextFactory.Create(http.User), dealId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.DealsPayments).WithName("RegisterDealPayment");
        group.MapPost("/{dealId:guid}/documents", async (Guid dealId, GenerateDealDocumentRequest request,
            HttpContext http, DealService service, CancellationToken ct) => Results.Ok(
            await service.GenerateDocumentAsync(ActorContextFactory.Create(http.User), dealId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.DealsDocuments).WithName("GenerateDealDocument");
        group.MapGet("/{dealId:guid}/documents/{documentId:guid}", async (Guid dealId, Guid documentId,
            HttpContext http, DealService service, CancellationToken ct) =>
        {
            var result = await service.DownloadDocumentAsync(ActorContextFactory.Create(http.User), dealId,
                documentId, http.TraceIdentifier, ct);
            return Results.File(result.Content, result.ContentType, result.FileName);
        }).RequireAuthorization(Permissions.DealsView).WithName("DownloadDealDocument");
        group.MapPost("/{dealId:guid}/ready-for-handover", async (Guid dealId, DealCommandRequest request,
            HttpContext http, DealService service, CancellationToken ct) => Results.Ok(
            await service.MarkReadyForHandoverAsync(ActorContextFactory.Create(http.User), dealId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.DealsHandover)
            .WithName("MarkDealReadyForHandover");
        group.MapPost("/{dealId:guid}/handover", async (Guid dealId, CompleteHandoverRequest request,
            HttpContext http, DealService service, CancellationToken ct) => Results.Ok(
            await service.CompleteHandoverAsync(ActorContextFactory.Create(http.User), dealId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.DealsHandover).WithName("CompleteDealHandover");
        group.MapPost("/{dealId:guid}/complete", async (Guid dealId, DealCommandRequest request,
            HttpContext http, DealService service, CancellationToken ct) => Results.Ok(
            await service.CompleteAsync(ActorContextFactory.Create(http.User), dealId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.DealsHandover).WithName("CompleteDeal");
        group.MapPost("/{dealId:guid}/cancel", async (Guid dealId, DealReasonCommandRequest request,
            HttpContext http, DealService service, CancellationToken ct) => Results.Ok(
            await service.CancelAsync(ActorContextFactory.Create(http.User), dealId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.DealsCancel).WithName("CancelDeal");
        return endpoints;
    }
}
