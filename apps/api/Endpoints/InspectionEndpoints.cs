using DealerOS.Api.Infrastructure;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Inspections.Application;
using DealerOS.Modules.Vehicles.Application;
using Microsoft.AspNetCore.Mvc;

namespace DealerOS.Api.Endpoints;

public static class InspectionEndpoints
{
    public static IEndpointRouteBuilder MapInspectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/inspections").RequireAuthorization();

        group.MapGet("/queue", async (HttpContext http, InspectionService service, CancellationToken ct) =>
            Results.Ok(await service.ListQueueAsync(ActorContextFactory.Create(http.User), ct)))
            .RequireAuthorization(Permissions.VehicleInspectionsView).WithName("ListInspectionQueue");

        group.MapGet("/{inspectionId:guid}", async (Guid inspectionId, HttpContext http,
            InspectionService service, CancellationToken ct) => Results.Ok(await service.GetAsync(
                ActorContextFactory.Create(http.User), inspectionId, ct)))
            .RequireAuthorization(Permissions.VehicleInspectionsView).WithName("GetInspection");

        group.MapPut("/{inspectionId:guid}/items/{itemId:guid}", async (Guid inspectionId, Guid itemId,
            SaveInspectionItemRequest request, HttpContext http, InspectionService service, CancellationToken ct) =>
            Results.Ok(await service.SaveItemAsync(ActorContextFactory.Create(http.User), inspectionId, itemId,
                request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.VehicleInspectionsEdit).WithName("SaveInspectionItem");

        group.MapPost("/{inspectionId:guid}/defects", async (Guid inspectionId, AddDefectRequest request,
            HttpContext http, InspectionService service, CancellationToken ct) => Results.Ok(await service.AddDefectAsync(
                ActorContextFactory.Create(http.User), inspectionId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.VehicleInspectionsEdit).WithName("AddInspectionDefect");

        group.MapPost("/{inspectionId:guid}/defects/{defectId:guid}/photos", async (Guid inspectionId, Guid defectId,
            HttpRequest request, HttpContext http, InspectionService service, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                throw new BadHttpRequestException("Ожидается multipart/form-data.", StatusCodes.Status400BadRequest);
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file")
                ?? throw new BadHttpRequestException("Файл обязателен.", StatusCodes.Status400BadRequest);
            if (file.Length > InspectionImageProcessor.MaxInputBytes)
                throw new BadHttpRequestException("Файл превышает лимит 8 МБ.", StatusCodes.Status413PayloadTooLarge);
            if (!Guid.TryParse(form["photoId"], out var photoId) || photoId == Guid.Empty)
                throw new BadHttpRequestException("photoId обязателен.", StatusCodes.Status400BadRequest);
            if (!long.TryParse(form["expectedVersion"], out var expectedVersion))
                throw new BadHttpRequestException("expectedVersion обязателен.", StatusCodes.Status400BadRequest);
            await using var stream = file.OpenReadStream();
            return Results.Ok(await service.AddPhotoAsync(ActorContextFactory.Create(http.User), inspectionId,
                defectId, photoId, expectedVersion, file.FileName, file.ContentType, file.Length, stream,
                http.TraceIdentifier, ct));
        }).RequireAuthorization(Permissions.VehicleInspectionsEdit)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(InspectionImageProcessor.MaxInputBytes + 65_536))
            .WithName("AddInspectionDefectPhoto");

        group.MapDelete("/{inspectionId:guid}/defects/{defectId:guid}", async (Guid inspectionId, Guid defectId,
            long expectedVersion, HttpContext http, InspectionService service, CancellationToken ct) => Results.Ok(
                await service.RemoveDraftDefectAsync(ActorContextFactory.Create(http.User), inspectionId, defectId,
                    expectedVersion, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.VehicleInspectionsEdit).WithName("RemoveDraftInspectionDefect");

        group.MapPost("/{inspectionId:guid}/complete", async (Guid inspectionId, CompleteInspectionRequest request,
            HttpContext http, InspectionService service, CancellationToken ct) => Results.Ok(await service.CompleteAsync(
                ActorContextFactory.Create(http.User), inspectionId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.VehicleInspectionsComplete).WithName("CompleteInspection");

        group.MapPost("/{inspectionId:guid}/cancel", async (Guid inspectionId, CancelInspectionRequest request,
            HttpContext http, InspectionService service, CancellationToken ct) => Results.Ok(await service.CancelAsync(
                ActorContextFactory.Create(http.User), inspectionId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.VehicleInspectionsCancel).WithName("CancelInspection");

        group.MapPost("/{inspectionId:guid}/corrections/start", async (Guid inspectionId, HttpContext http,
            InspectionService service, CancellationToken ct) => Results.Ok(await service.StartCorrectionAsync(
                ActorContextFactory.Create(http.User), inspectionId, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.VehicleInspectionsComplete).WithName("StartInspectionCorrection");

        group.MapGet("/{inspectionId:guid}/defects/{defectId:guid}/photos/{photoId:guid}", async (
            Guid inspectionId, Guid defectId, Guid photoId, HttpContext http, InspectionService service,
            CancellationToken ct) =>
        {
            var photo = await service.DownloadPhotoAsync(ActorContextFactory.Create(http.User), inspectionId,
                defectId, photoId, ct);
            return Results.File(photo.Content, photo.ContentType, photo.FileName, enableRangeProcessing: false);
        }).RequireAuthorization(Permissions.VehicleInspectionsView).WithName("DownloadInspectionDefectPhoto");

        endpoints.MapGet("/api/vehicles/{vehicleId:guid}/inspections", async (Guid vehicleId, HttpContext http,
            InspectionService service, CancellationToken ct) => Results.Ok(await service.ListForVehicleAsync(
                ActorContextFactory.Create(http.User), vehicleId, ct)))
            .RequireAuthorization(Permissions.VehicleInspectionsView).WithName("ListVehicleInspections");

        endpoints.MapPost("/api/vehicles/{vehicleId:guid}/inspections/start", async (Guid vehicleId,
            StartInspectionRequest request, HttpContext http, InspectionService service, CancellationToken ct) =>
            Results.Ok(await service.StartAsync(ActorContextFactory.Create(http.User), vehicleId, request,
                http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.VehicleInspectionsCreate).WithName("StartVehicleInspection");

        var templates = endpoints.MapGroup("/api/inspection-templates").RequireAuthorization();
        templates.MapGet("/", async (HttpContext http, InspectionService service, CancellationToken ct) => Results.Ok(
            await service.ListTemplatesAsync(ActorContextFactory.Create(http.User), ct)))
            .RequireAuthorization(Permissions.VehicleInspectionsView).WithName("ListInspectionTemplates");
        templates.MapPost("/versions", async (CreateTemplateRequest request, HttpContext http,
            InspectionService service, CancellationToken ct) => Results.Created("/api/inspection-templates",
                await service.CreateTemplateAsync(ActorContextFactory.Create(http.User), request,
                    http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.VehicleInspectionsManageTemplates)
            .WithName("CreateInspectionTemplateVersion");

        return endpoints;
    }
}
