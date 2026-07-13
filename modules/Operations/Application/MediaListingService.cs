using System.Text.Json;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Inspections.Application;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Vehicles.Application;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Operations.Application;

public sealed class MediaListingService(IQualityListingStore store, IInspectionPhotoStorage storage,
    IInspectionImageProcessor imageProcessor, IAuditWriter audit, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly VehicleMediaCategory[] RequiredListingCategories =
        [VehicleMediaCategory.Exterior, VehicleMediaCategory.Interior, VehicleMediaCategory.DamageHistory];

    public async Task<VehicleMediaResponse> UploadMediaAsync(ActorContext actor, Guid vehicleId, Guid mediaId,
        VehicleMediaCategory category, Stream content, long declaredLength, string originalFileName,
        string? contentType, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ListingsEdit);
        var vehicle = await FindReadyVehicleAsync(actor, vehicleId, cancellationToken);
        var registered = await store.FindMediaRegistrationAsync(mediaId, cancellationToken);
        if (registered is not null)
        {
            if (registered.OrganizationId == actor.OrganizationId && registered.VehicleId == vehicleId)
                return Map(registered.Media);
            throw new ConflictException("media.id_conflict", "Media ID уже связан с другим автомобилем.");
        }

        var normalized = await imageProcessor.NormalizeAsync(content, declaredLength,
            originalFileName, contentType, cancellationToken);
        await using var normalizedContent = normalized.Content;
        var attemptId = Guid.NewGuid();
        var objectKey = $"{actor.OrganizationId:N}/vehicles/{vehicleId:N}/media/{mediaId:N}/{attemptId:N}.{normalized.Extension}";
        await storage.PutAsync(objectKey, normalized.Content, normalized.SizeBytes, normalized.ContentType,
            cancellationToken);
        var existing = await store.ListMediaAsync(actor.OrganizationId, vehicleId, cancellationToken);
        var media = new VehicleMedia(mediaId, actor.OrganizationId, vehicle.BranchId, vehicleId, category,
            objectKey, Path.GetFileName(originalFileName), normalized.ContentType, normalized.SizeBytes,
            existing.Count == 0 ? 10 : existing.Max(x => x.SortOrder) + 10, actor.UserId, timeProvider.GetUtcNow());
        await store.AddMediaAsync(media, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "listing.media_uploaded", "VehicleMedia", media.Id,
            null, JsonSerializer.Serialize(new { vehicleId, category, media.OriginalFileName }), correlationId,
            timeProvider.GetUtcNow());
        try
        {
            await store.SaveChangesAsync(cancellationToken);
            return Map(media);
        }
        catch (ConflictException)
        {
            store.ResetTracking();
            try
            {
                await storage.DeleteAsync(objectKey, CancellationToken.None);
            }
            catch (StorageUnavailableException)
            {
                await store.QueueObjectDeletionAsync(actor.OrganizationId, objectKey, timeProvider.GetUtcNow(),
                    CancellationToken.None);
                await store.SaveChangesAsync(CancellationToken.None);
                store.ResetTracking();
            }
            registered = await store.FindMediaRegistrationAsync(mediaId, CancellationToken.None);
            if (registered is not null && registered.OrganizationId == actor.OrganizationId
                && registered.VehicleId == vehicleId) return Map(registered.Media);
            throw;
        }
    }

    public async Task<VehicleMediaDownload> DownloadMediaAsync(ActorContext actor, Guid vehicleId, Guid mediaId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ListingsView);
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        DemandBranch(actor, vehicle.BranchId);
        var media = await store.FindMediaAsync(actor.OrganizationId, mediaId, cancellationToken);
        if (media is null || media.VehicleId != vehicleId) throw new NotFoundException("Медиа не найдено.");
        return new VehicleMediaDownload(await storage.GetAsync(media.ObjectKey, cancellationToken),
            media.ContentType, media.OriginalFileName);
    }

    public async Task<IReadOnlyList<VehicleMediaResponse>> ListMediaAsync(ActorContext actor, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ListingsView);
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        DemandBranch(actor, vehicle.BranchId);
        return (await store.ListMediaAsync(actor.OrganizationId, vehicleId, cancellationToken)).Select(Map).ToArray();
    }

    public async Task<VehicleMediaResponse> SetOrderAsync(ActorContext actor, Guid vehicleId, Guid mediaId,
        UpdateMediaOrderRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ListingsEdit);
        await FindReadyVehicleAsync(actor, vehicleId, cancellationToken);
        var media = await FindMediaAsync(actor, vehicleId, mediaId, cancellationToken);
        media.SetSortOrder(request.SortOrder, request.ExpectedVersion);
        audit.Write(actor.OrganizationId, actor.UserId, "listing.media_order_changed", "VehicleMedia", media.Id,
            null, JsonSerializer.Serialize(new { request.SortOrder }), correlationId, timeProvider.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken); return Map(media);
    }

    public async Task<VehicleMediaResponse> SetCoverAsync(ActorContext actor, Guid vehicleId, Guid mediaId,
        SetMediaCoverRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ListingsEdit);
        await FindReadyVehicleAsync(actor, vehicleId, cancellationToken);
        var mediaItems = await store.ListMediaAsync(actor.OrganizationId, vehicleId, cancellationToken);
        var target = mediaItems.SingleOrDefault(x => x.Id == mediaId) ?? throw new NotFoundException("Медиа не найдено.");
        if (target.Version != request.ExpectedVersion)
            throw new ConflictException("media.version_conflict", "Медиа изменено конкурентно.");
        foreach (var current in mediaItems.Where(x => x.IsCover && x.Id != target.Id))
            current.SetCover(false, current.Version);
        if (!target.IsCover) target.SetCover(true, target.Version);
        audit.Write(actor.OrganizationId, actor.UserId, "listing.media_cover_changed", "VehicleMedia", target.Id,
            null, JsonSerializer.Serialize(new { vehicleId }), correlationId, timeProvider.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken); return Map(target);
    }

    public async Task<ListingContentResponse> CreateListingAsync(ActorContext actor, Guid vehicleId,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ListingsEdit);
        var vehicle = await FindReadyVehicleAsync(actor, vehicleId, cancellationToken);
        var latest = await store.FindLatestListingAsync(actor.OrganizationId, vehicleId, cancellationToken);
        if (latest?.Status == ListingContentStatus.Draft) return Map(latest);
        if (latest is not null)
            throw new ConflictException("listing.revision_required", "Listing Ready неизменяем; новая ревизия пока создаётся отдельным процессом.");
        var now = timeProvider.GetUtcNow();
        var listing = ListingContent.Create(actor.OrganizationId, vehicle.BranchId, vehicle.Id, 1, vehicle.Make,
            vehicle.Model, vehicle.Year, vehicle.MileageKm, actor.UserId, now);
        await store.AddListingAsync(listing, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "listing.created", "ListingContent", listing.Id, null,
            JsonSerializer.Serialize(new { vehicleId }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return Map(listing);
    }

    public async Task<ListingContentResponse> GetListingAsync(ActorContext actor, Guid listingId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ListingsView); return Map(await FindListingAsync(actor, listingId, cancellationToken));
    }

    public async Task<ListingContentResponse?> GetLatestListingAsync(ActorContext actor, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ListingsView);
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        DemandBranch(actor, vehicle.BranchId);
        var listing = await store.FindLatestListingAsync(actor.OrganizationId, vehicleId, cancellationToken);
        return listing is null ? null : Map(listing);
    }

    public async Task<ListingContentResponse> UpdateListingAsync(ActorContext actor, Guid listingId,
        UpdateListingContentRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ListingsEdit);
        var listing = await FindListingAsync(actor, listingId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var changed = listing.Update(request.CommandId, request.Equipment, request.Advantages,
            request.ConditionDescription, request.PublicPriceAmount, request.Currency, request.TemplateName,
            request.TemplateVersion, actor.UserId, request.ExpectedVersion, now);
        if (!changed) return Map(listing);
        audit.Write(actor.OrganizationId, actor.UserId, "listing.content_updated", "ListingContent", listing.Id,
            null, JsonSerializer.Serialize(request), correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return Map(listing);
    }

    public async Task<ListingContentResponse> MarkReadyAsync(ActorContext actor, Guid listingId,
        ListingCommandRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ListingsEdit);
        var listing = await FindListingAsync(actor, listingId, cancellationToken);
        var vehicle = await FindReadyVehicleAsync(actor, listing.VehicleId, cancellationToken);
        var media = await store.ListMediaAsync(actor.OrganizationId, listing.VehicleId, cancellationToken);
        var missing = RequiredListingCategories.Where(category => media.All(x => x.Category != category)).ToArray();
        if (missing.Length > 0)
            throw new DomainException("listing.required_media_missing",
                $"Не хватает обязательных категорий кадров: {string.Join(", ", missing)}.");
        if (media.Count(x => x.IsCover) != 1)
            throw new DomainException("listing.cover_required", "Выберите ровно одну обложку.");
        var publicMedia = media.Where(x => x.Category != VehicleMediaCategory.DocumentsInternal)
            .OrderBy(x => x.SortOrder).Select(x => new
            {
                x.Id,
                category = x.Category.ToString(),
                x.IsCover,
                downloadUrl = $"/api/vehicles/{vehicle.Id}/media/{x.Id}"
            }).ToArray();
        var snapshot = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            listing.Id,
            listing.Revision,
            vehicle = new
            {
                vehicle.Id,
                listing.VehicleMake,
                listing.VehicleModel,
                listing.VehicleYear,
                listing.MileageKm
            },
            listing.Equipment,
            listing.Advantages,
            listing.ConditionDescription,
            listing.PublicPriceAmount,
            listing.Currency,
            listing.TemplateName,
            listing.TemplateVersion,
            media = publicMedia
        }, SnapshotJsonOptions);
        var now = timeProvider.GetUtcNow();
        if (!listing.MarkReady(request.CommandId, snapshot, actor.UserId, request.ExpectedVersion, now))
            return Map(listing);
        audit.Write(actor.OrganizationId, actor.UserId, "listing.ready", "ListingContent", listing.Id, null,
            snapshot, correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return Map(listing);
    }

    public async Task<ListingExportResponse> ExportAsync(ActorContext actor, Guid listingId,
        PublicationCommandRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ListingsPublish);
        var listing = await FindListingAsync(actor, listingId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (listing.Export(request.CommandId, request.Channel, actor.UserId, now))
        {
            audit.Write(actor.OrganizationId, actor.UserId, "listing.exported", "ListingContent", listing.Id,
                null, JsonSerializer.Serialize(new { request.Channel }), correlationId, now);
            await store.SaveChangesAsync(cancellationToken);
        }
        return new ListingExportResponse($"dealeros-listing-{listing.VehicleId:N}-r{listing.Revision}.json",
            "application/json", listing.SnapshotJson!, Map(listing));
    }

    public Task<ListingContentResponse> PublishAsync(ActorContext actor, Guid listingId,
        PublicationCommandRequest request, string correlationId, CancellationToken cancellationToken) =>
        PublicationAsync(actor, listingId, request, "listing.published", (listing, now) => listing.Publish(
            request.CommandId, request.Channel, request.ExternalId, request.ExternalUrl, actor.UserId, now),
            correlationId, cancellationToken);

    public Task<ListingContentResponse> FailAsync(ActorContext actor, Guid listingId,
        PublicationCommandRequest request, string correlationId, CancellationToken cancellationToken) =>
        PublicationAsync(actor, listingId, request, "listing.failed", (listing, now) => listing.Fail(
            request.CommandId, request.Channel, request.Error, actor.UserId, now), correlationId, cancellationToken);

    public Task<ListingContentResponse> UnpublishAsync(ActorContext actor, Guid listingId,
        PublicationCommandRequest request, string correlationId, CancellationToken cancellationToken) =>
        PublicationAsync(actor, listingId, request, "listing.unpublished", (listing, now) => listing.Unpublish(
            request.CommandId, request.Channel, actor.UserId, now), correlationId, cancellationToken);

    private async Task<ListingContentResponse> PublicationAsync(ActorContext actor, Guid listingId,
        PublicationCommandRequest request, string operation, Func<ListingContent, DateTimeOffset, bool> command,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ListingsPublish);
        var listing = await FindListingAsync(actor, listingId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!command(listing, now)) return Map(listing);
        audit.Write(actor.OrganizationId, actor.UserId, operation, "ListingContent", listing.Id, null,
            JsonSerializer.Serialize(request), correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return Map(listing);
    }

    private async Task<Vehicle> FindReadyVehicleAsync(ActorContext actor, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        DemandBranch(actor, vehicle.BranchId);
        if (vehicle.Status != VehicleStatus.ReadyForSale)
            throw new DomainException("listing.vehicle_not_ready", "Медиа и listing доступны после успешного QC.");
        return vehicle;
    }
    private async Task<VehicleMedia> FindMediaAsync(ActorContext actor, Guid vehicleId, Guid mediaId,
        CancellationToken cancellationToken)
    {
        var media = await store.FindMediaAsync(actor.OrganizationId, mediaId, cancellationToken);
        if (media is null || media.VehicleId != vehicleId) throw new NotFoundException("Медиа не найдено.");
        DemandBranch(actor, media.BranchId); return media;
    }
    private async Task<ListingContent> FindListingAsync(ActorContext actor, Guid listingId,
        CancellationToken cancellationToken)
    {
        var listing = await store.FindListingAsync(actor.OrganizationId, listingId, cancellationToken)
            ?? throw new NotFoundException("Listing не найден.");
        DemandBranch(actor, listing.BranchId); return listing;
    }
    private static VehicleMediaResponse Map(VehicleMedia media) => new(media.Id, media.VehicleId,
        media.Category.ToString(), media.OriginalFileName, media.ContentType, media.SizeBytes, media.SortOrder,
        media.IsCover, media.Version, $"/api/vehicles/{media.VehicleId}/media/{media.Id}", media.CreatedAt);
    private static ListingContentResponse Map(ListingContent listing) => new(listing.Id, listing.VehicleId,
        listing.Revision, listing.Status.ToString(), listing.VehicleMake, listing.VehicleModel, listing.VehicleYear,
        listing.MileageKm, listing.Equipment, listing.Advantages, listing.ConditionDescription,
        listing.PublicPriceAmount, listing.Currency, listing.TemplateName, listing.TemplateVersion,
        listing.SnapshotJson, listing.Version, listing.ReadyAt,
        listing.Publications.OrderBy(x => x.Channel).Select(x => new ChannelPublicationResponse(x.Id, x.Channel,
            x.Status.ToString(), x.ExternalId, x.ExternalUrl, x.Error, x.ExportedAt, x.PublishedAt,
            x.UnpublishedAt)).ToArray(), listing.History.OrderBy(x => x.OccurredAt).Select(x =>
            new ListingHistoryResponse(x.CommandId, x.Action, x.PublicPriceAmount, x.Currency,
                x.OccurredAt)).ToArray());
    private static void Demand(ActorContext actor, string permission)
    { if (!actor.Permissions.Contains(permission)) throw new ForbiddenException("Недостаточно прав для media/listing."); }
    private static void DemandBranch(ActorContext actor, Guid branchId)
    { if (!actor.BranchIds.Contains(branchId)) throw new ForbiddenException("Филиал недоступен пользователю."); }
}
