using DealerOS.Api.Infrastructure;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Operations.Application;
using DealerOS.Modules.Operations.Domain;
using Microsoft.AspNetCore.Mvc;

namespace DealerOS.Api.Endpoints;

public static class QualityListingEndpoints
{
    public static IEndpointRouteBuilder MapQualityListingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var quality = endpoints.MapGroup("/api/quality-checks").RequireAuthorization();
        quality.MapGet("/queue", async (HttpContext http, QualityControlService service, CancellationToken ct) =>
            Results.Ok(await service.ListQueueAsync(ActorContextFactory.Create(http.User), ct)))
            .RequireAuthorization(Permissions.QualityView).WithName("ListQualityQueue");
        quality.MapPost("/executions/{executionId:guid}", async (Guid executionId, HttpContext http,
            QualityControlService service, CancellationToken ct) => Results.Ok(await service.CreateAsync(
                ActorContextFactory.Create(http.User), executionId, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.QualityCreate).WithName("CreateQualityCheck");
        quality.MapGet("/{qualityCheckId:guid}", async (Guid qualityCheckId, HttpContext http,
            QualityControlService service, CancellationToken ct) => Results.Ok(await service.GetAsync(
                ActorContextFactory.Create(http.User), qualityCheckId, ct)))
            .RequireAuthorization(Permissions.QualityView).WithName("GetQualityCheck");
        quality.MapPost("/{qualityCheckId:guid}/observations", async (Guid qualityCheckId,
            AddQualityObservationRequest request, HttpContext http, QualityControlService service,
            CancellationToken ct) => Results.Ok(await service.AddObservationAsync(
                ActorContextFactory.Create(http.User), qualityCheckId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.QualityDecide).WithName("AddQualityObservation");
        quality.MapPost("/{qualityCheckId:guid}/pass", async (Guid qualityCheckId, QualityDecisionRequest request,
            HttpContext http, QualityControlService service, CancellationToken ct) => Results.Ok(
                await service.PassAsync(ActorContextFactory.Create(http.User), qualityCheckId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.QualityDecide)
            .WithName("PassQualityCheck");
        quality.MapPost("/{qualityCheckId:guid}/rework", async (Guid qualityCheckId, QualityDecisionRequest request,
            HttpContext http, QualityControlService service, CancellationToken ct) => Results.Ok(
                await service.ReworkAsync(ActorContextFactory.Create(http.User), qualityCheckId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.QualityDecide)
            .WithName("RequestQualityRework");
        quality.MapPost("/{qualityCheckId:guid}/reject", async (Guid qualityCheckId, QualityDecisionRequest request,
            HttpContext http, QualityControlService service, CancellationToken ct) => Results.Ok(
                await service.RejectAsync(ActorContextFactory.Create(http.User), qualityCheckId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.QualityDecide)
            .WithName("RejectQualityCheck");
        endpoints.MapGet("/api/vehicles/{vehicleId:guid}/quality-checks", async (Guid vehicleId, HttpContext http,
            QualityControlService service, CancellationToken ct) => Results.Ok(await service.ListAsync(
                ActorContextFactory.Create(http.User), vehicleId, ct))).RequireAuthorization(Permissions.QualityView)
            .WithName("ListVehicleQualityChecks");

        var vehicles = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}").RequireAuthorization();
        vehicles.MapPost("/media", async (Guid vehicleId, HttpRequest request, HttpContext http,
            MediaListingService service, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                throw new BadHttpRequestException("Ожидается multipart/form-data.", StatusCodes.Status400BadRequest);
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file")
                ?? throw new BadHttpRequestException("Файл обязателен.", StatusCodes.Status400BadRequest);
            if (file.Length > InspectionImageProcessor.MaxInputBytes)
                throw new BadHttpRequestException("Файл превышает лимит 8 МБ.", StatusCodes.Status413PayloadTooLarge);
            if (!Guid.TryParse(form["mediaId"], out var mediaId) || mediaId == Guid.Empty)
                throw new BadHttpRequestException("mediaId обязателен.", StatusCodes.Status400BadRequest);
            if (!Enum.TryParse<VehicleMediaCategory>(form["category"], true, out var category))
                throw new BadHttpRequestException("category недопустима.", StatusCodes.Status400BadRequest);
            await using var stream = file.OpenReadStream();
            return Results.Ok(await service.UploadMediaAsync(ActorContextFactory.Create(http.User), vehicleId,
                mediaId, category, stream, file.Length, file.FileName, file.ContentType, http.TraceIdentifier, ct));
        }).RequireAuthorization(Permissions.ListingsEdit).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(InspectionImageProcessor.MaxInputBytes + 65_536))
            .WithName("UploadVehicleMedia");
        vehicles.MapGet("/media", async (Guid vehicleId, HttpContext http, MediaListingService service,
            CancellationToken ct) => Results.Ok(await service.ListMediaAsync(ActorContextFactory.Create(http.User),
                vehicleId, ct))).RequireAuthorization(Permissions.ListingsView).WithName("ListVehicleMedia");
        vehicles.MapGet("/media/{mediaId:guid}", async (Guid vehicleId, Guid mediaId, HttpContext http,
            MediaListingService service, CancellationToken ct) =>
        {
            var result = await service.DownloadMediaAsync(ActorContextFactory.Create(http.User), vehicleId,
                mediaId, ct);
            return Results.File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: false);
        }).RequireAuthorization(Permissions.ListingsView).WithName("DownloadVehicleMedia");
        vehicles.MapPost("/media/{mediaId:guid}/order", async (Guid vehicleId, Guid mediaId,
            UpdateMediaOrderRequest request, HttpContext http, MediaListingService service, CancellationToken ct) =>
            Results.Ok(await service.SetOrderAsync(ActorContextFactory.Create(http.User), vehicleId, mediaId,
                request, http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ListingsEdit)
            .WithName("SetVehicleMediaOrder");
        vehicles.MapPost("/media/{mediaId:guid}/cover", async (Guid vehicleId, Guid mediaId,
            SetMediaCoverRequest request, HttpContext http, MediaListingService service, CancellationToken ct) =>
            Results.Ok(await service.SetCoverAsync(ActorContextFactory.Create(http.User), vehicleId, mediaId,
                request, http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ListingsEdit)
            .WithName("SetVehicleMediaCover");
        vehicles.MapPost("/listing", async (Guid vehicleId, HttpContext http, MediaListingService service,
            CancellationToken ct) => Results.Ok(await service.CreateListingAsync(
                ActorContextFactory.Create(http.User), vehicleId, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.ListingsEdit).WithName("CreateVehicleListing");
        vehicles.MapGet("/listing", async (Guid vehicleId, HttpContext http, MediaListingService service,
            CancellationToken ct) => Results.Ok(await service.GetLatestListingAsync(
                ActorContextFactory.Create(http.User), vehicleId, ct)))
            .RequireAuthorization(Permissions.ListingsView).WithName("GetLatestVehicleListing");

        var listings = endpoints.MapGroup("/api/listings").RequireAuthorization();
        listings.MapGet("/{listingId:guid}", async (Guid listingId, HttpContext http,
            MediaListingService service, CancellationToken ct) => Results.Ok(await service.GetListingAsync(
                ActorContextFactory.Create(http.User), listingId, ct))).RequireAuthorization(Permissions.ListingsView)
            .WithName("GetListing");
        listings.MapPut("/{listingId:guid}", async (Guid listingId, UpdateListingContentRequest request,
            HttpContext http, MediaListingService service, CancellationToken ct) => Results.Ok(
                await service.UpdateListingAsync(ActorContextFactory.Create(http.User), listingId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ListingsEdit)
            .WithName("UpdateListingContent");
        listings.MapPost("/{listingId:guid}/ready", async (Guid listingId, ListingCommandRequest request,
            HttpContext http, MediaListingService service, CancellationToken ct) => Results.Ok(
                await service.MarkReadyAsync(ActorContextFactory.Create(http.User), listingId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ListingsEdit)
            .WithName("MarkListingReady");
        listings.MapPost("/{listingId:guid}/export", async (Guid listingId, PublicationCommandRequest request,
            HttpContext http, MediaListingService service, CancellationToken ct) => Results.Ok(
                await service.ExportAsync(ActorContextFactory.Create(http.User), listingId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ListingsPublish)
            .WithName("ExportListing");
        listings.MapPost("/{listingId:guid}/publish", async (Guid listingId, PublicationCommandRequest request,
            HttpContext http, MediaListingService service, CancellationToken ct) => Results.Ok(
                await service.PublishAsync(ActorContextFactory.Create(http.User), listingId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ListingsPublish)
            .WithName("ConfirmListingPublished");
        listings.MapPost("/{listingId:guid}/fail", async (Guid listingId, PublicationCommandRequest request,
            HttpContext http, MediaListingService service, CancellationToken ct) => Results.Ok(
                await service.FailAsync(ActorContextFactory.Create(http.User), listingId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ListingsPublish)
            .WithName("MarkListingPublicationFailed");
        listings.MapPost("/{listingId:guid}/unpublish", async (Guid listingId, PublicationCommandRequest request,
            HttpContext http, MediaListingService service, CancellationToken ct) => Results.Ok(
                await service.UnpublishAsync(ActorContextFactory.Create(http.User), listingId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ListingsPublish)
            .WithName("MarkListingUnpublished");
        return endpoints;
    }
}
