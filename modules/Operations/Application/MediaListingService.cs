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
    public const int MaxPhotosPerVehicle = 100;
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly VehicleMediaCategory[] RequiredListingCategories =
        [VehicleMediaCategory.MainView, VehicleMediaCategory.Interior, VehicleMediaCategory.Defect];

    public async Task<VehicleMediaResponse> UploadMediaAsync(ActorContext actor, Guid vehicleId, Guid mediaId,
        VehicleMediaCategory? category, Stream content, long declaredLength, string originalFileName,
        string? contentType, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesPhotosUpload);
        var vehicle = await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
        var registered = await store.FindMediaRegistrationAsync(mediaId, cancellationToken);
        if (registered is not null)
        {
            if (registered.OrganizationId == actor.OrganizationId && registered.VehicleId == vehicleId
                && registered.Media.SourceInspectionPhotoId is null)
                return Map(registered.Media);
            throw new ConflictException("media.id_conflict", "Media ID уже связан с другим автомобилем.");
        }
        await EnsureCapacityAsync(actor.OrganizationId, vehicleId, cancellationToken);

        var processed = await imageProcessor.CreateVehicleGallerySetAsync(content, declaredLength,
            originalFileName, contentType, cancellationToken);
        var root = $"organizations/{actor.OrganizationId:N}/vehicles/{vehicleId:N}/gallery/{mediaId:N}/{Guid.NewGuid():N}";
        var keys = ImageKeys.Create(root, processed.Original.Extension);
        await UploadSetAsync(actor.OrganizationId, keys, processed, cancellationToken);
        return await PersistMediaAsync(actor, vehicle, mediaId, category, Path.GetFileName(originalFileName),
            keys, processed, null, true, "vehicle.photo_uploaded", correlationId, cancellationToken);
    }

    public async Task<VehicleMediaResponse> ImportInspectionPhotoAsync(ActorContext actor, Guid vehicleId,
        ImportInspectionPhotoRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesPhotosUpload);
        var vehicle = await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
        var registered = await store.FindMediaRegistrationAsync(request.MediaId, cancellationToken);
        if (registered is not null)
        {
            if (registered.OrganizationId == actor.OrganizationId && registered.VehicleId == vehicleId
                && registered.Media.SourceInspectionPhotoId == request.InspectionPhotoId)
                return Map(registered.Media);
            throw new ConflictException("media.id_conflict",
                "Media ID уже связан с другим автомобилем или источником фотографии.");
        }
        await EnsureCapacityAsync(actor.OrganizationId, vehicleId, cancellationToken);
        var source = await store.FindInspectionMediaSourceAsync(actor.OrganizationId, vehicleId,
            request.InspectionPhotoId, cancellationToken)
            ?? throw new NotFoundException("Фотография завершённого осмотра не найдена.");
        DemandBranch(actor, source.BranchId);

        await using var sourceStream = await storage.GetAsync(source.ObjectKey, cancellationToken);
        var processed = await imageProcessor.CreateVehicleGallerySetAsync(sourceStream, source.SizeBytes,
            source.OriginalFileName, source.ContentType, cancellationToken);
        var root = $"organizations/{actor.OrganizationId:N}/vehicles/{vehicleId:N}/gallery/{request.MediaId:N}/{Guid.NewGuid():N}";
        var keys = ImageKeys.Create(root, processed.Original.Extension, source.ObjectKey);
        await UploadDerivativesAsync(actor.OrganizationId, keys, processed, cancellationToken);
        try
        {
            return await PersistMediaAsync(actor, vehicle, request.MediaId,
                request.Category ?? VehicleMediaCategory.Defect, source.OriginalFileName, keys, processed,
                source.PhotoId, false, "vehicle.photo_imported_from_inspection", correlationId, cancellationToken);
        }
        catch (ConflictException)
        {
            store.ResetTracking();
            var existing = (await store.ListMediaAsync(actor.OrganizationId, vehicleId, CancellationToken.None))
                .SingleOrDefault(x => x.SourceInspectionPhotoId == source.PhotoId);
            if (existing is not null) return Map(existing);
            throw;
        }
    }

    public async Task<IReadOnlyList<InspectionMediaSourceResponse>> ListInspectionSourcesAsync(ActorContext actor,
        Guid vehicleId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesPhotosView);
        await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
        return await store.ListInspectionMediaSourcesAsync(actor.OrganizationId, vehicleId, actor.BranchIds,
            cancellationToken);
    }

    public async Task<VehicleMediaDownload> DownloadMediaAsync(ActorContext actor, Guid vehicleId, Guid mediaId,
        VehicleMediaVariant variant, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesPhotosView);
        await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
        var media = await store.FindMediaAsync(actor.OrganizationId, mediaId, cancellationToken);
        if (media is null || media.VehicleId != vehicleId) throw new NotFoundException("Медиа не найдено.");
        var objectKey = variant switch
        {
            VehicleMediaVariant.Thumbnail => media.ThumbnailObjectKey,
            VehicleMediaVariant.Medium => media.MediumObjectKey,
            VehicleMediaVariant.Large => media.LargeObjectKey,
            _ => media.ObjectKey
        };
        return new VehicleMediaDownload(await storage.GetAsync(objectKey, cancellationToken),
            media.ContentType, media.OriginalFileName);
    }

    public async Task<IReadOnlyList<VehicleMediaResponse>> ListMediaAsync(ActorContext actor, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesPhotosView);
        await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
        return (await store.ListMediaAsync(actor.OrganizationId, vehicleId, cancellationToken)).Select(Map).ToArray();
    }

    public async Task<VehicleMediaResponse> SetOrderAsync(ActorContext actor, Guid vehicleId, Guid mediaId,
        UpdateMediaOrderRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesPhotosManage);
        await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
        var media = await FindMediaAsync(actor, vehicleId, mediaId, cancellationToken);
        media.SetSortOrder(request.SortOrder, request.ExpectedVersion);
        audit.Write(actor.OrganizationId, actor.UserId, "vehicle.photo_order_changed", "VehicleMedia", media.Id,
            null, JsonSerializer.Serialize(new { request.SortOrder }), correlationId, timeProvider.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken); return Map(media);
    }

    public async Task<IReadOnlyList<VehicleMediaResponse>> ReorderAsync(ActorContext actor, Guid vehicleId,
        ReorderVehicleMediaRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesPhotosManage);
        await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
        if (request.Items.Count == 0 || request.Items.Count > MaxPhotosPerVehicle
            || request.Items.Select(x => x.MediaId).Distinct().Count() != request.Items.Count)
            throw new DomainException("media.invalid_order", "Передан некорректный порядок фотографий.");
        var media = await store.ListMediaAsync(actor.OrganizationId, vehicleId, cancellationToken);
        var byId = media.ToDictionary(x => x.Id);
        foreach (var item in request.Items)
        {
            if (!byId.TryGetValue(item.MediaId, out var current)) throw new NotFoundException("Медиа не найдено.");
            current.SetSortOrder(item.SortOrder, item.ExpectedVersion);
        }
        audit.Write(actor.OrganizationId, actor.UserId, "vehicle.photo_order_changed", "Vehicle", vehicleId,
            null, JsonSerializer.Serialize(request.Items), correlationId, timeProvider.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);
        return media.OrderBy(x => x.SortOrder).ThenBy(x => x.CreatedAt).Select(Map).ToArray();
    }

    public async Task<VehicleMediaResponse> SetCoverAsync(ActorContext actor, Guid vehicleId, Guid mediaId,
        SetMediaCoverRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesPhotosManage);
        await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
        var mediaItems = await store.ListMediaAsync(actor.OrganizationId, vehicleId, cancellationToken);
        var target = mediaItems.SingleOrDefault(x => x.Id == mediaId) ?? throw new NotFoundException("Медиа не найдено.");
        if (target.Version != request.ExpectedVersion)
            throw new ConflictException("media.version_conflict", "Медиа изменено конкурентно.");
        foreach (var current in mediaItems.Where(x => x.IsCover && x.Id != target.Id))
            current.SetCover(false, current.Version);
        if (!target.IsCover) target.SetCover(true, target.Version);
        audit.Write(actor.OrganizationId, actor.UserId, "vehicle.photo_cover_changed", "VehicleMedia", target.Id,
            null, JsonSerializer.Serialize(new { vehicleId }), correlationId, timeProvider.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken); return Map(target);
    }

    public async Task<VehicleMediaResponse> UpdateMetadataAsync(ActorContext actor, Guid vehicleId, Guid mediaId,
        UpdateMediaMetadataRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesPhotosManage);
        await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
        var media = await FindMediaAsync(actor, vehicleId, mediaId, cancellationToken);
        media.UpdateMetadata(request.Category, request.Caption, request.FocalPointX, request.FocalPointY,
            request.ExpectedVersion);
        audit.Write(actor.OrganizationId, actor.UserId, "vehicle.photo_metadata_changed", "VehicleMedia", media.Id,
            null, JsonSerializer.Serialize(new
            {
                vehicleId,
                request.Category,
                request.Caption,
                request.FocalPointX,
                request.FocalPointY
            }), correlationId, timeProvider.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken); return Map(media);
    }

    public async Task<VehicleMediaResponse> SetListingSelectionAsync(ActorContext actor, Guid vehicleId,
        Guid mediaId, SetMediaListingSelectionRequest request, string correlationId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesPhotosManage);
        await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
        if (await store.HasImmutableListingAsync(actor.OrganizationId, vehicleId, cancellationToken))
            throw new ConflictException("media.immutable_listing",
                "Отбор фотографий зафиксирован в готовом объявлении и больше не изменяется.");
        var media = await FindMediaAsync(actor, vehicleId, mediaId, cancellationToken);
        media.SetIncludedInListing(request.IsIncluded, request.ExpectedVersion);
        audit.Write(actor.OrganizationId, actor.UserId, "vehicle.photo_listing_selection_changed",
            "VehicleMedia", media.Id, null, JsonSerializer.Serialize(new { vehicleId, request.IsIncluded }),
            correlationId, timeProvider.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken); return Map(media);
    }

    public async Task DeleteAsync(ActorContext actor, Guid vehicleId, Guid mediaId, DeleteMediaRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesPhotosManage);
        await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
        var media = await FindMediaAsync(actor, vehicleId, mediaId, cancellationToken);
        media.ValidateVersion(request.ExpectedVersion);
        if (media.IsIncludedInListing && await store.HasImmutableListingAsync(actor.OrganizationId, vehicleId,
                cancellationToken))
            throw new ConflictException("media.immutable_listing",
                "Фотография входит в неизменяемое объявление и не может быть удалена.");
        var keys = media.OwnedObjectKeys().Distinct(StringComparer.Ordinal).ToArray();
        var now = timeProvider.GetUtcNow();
        store.RemoveMedia(media);
        foreach (var key in keys)
            await store.QueueObjectDeletionAsync(actor.OrganizationId, key, now, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "vehicle.photo_deleted", "VehicleMedia", media.Id,
            null, JsonSerializer.Serialize(new { vehicleId, media.SourceInspectionPhotoId }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        await DeleteQueuedObjectsAsync(actor.OrganizationId, keys);
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
        await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
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
        var media = (await store.ListMediaAsync(actor.OrganizationId, listing.VehicleId, cancellationToken))
            .Where(x => x.IsIncludedInListing).ToArray();
        var missing = RequiredListingCategories.Where(category => media.All(x => x.Category != category)).ToArray();
        if (missing.Length > 0)
            throw new DomainException("listing.required_media_missing",
                $"Не хватает выбранных для объявления категорий кадров: {string.Join(", ", missing)}.");
        if (media.Count(x => x.IsCover) != 1)
            throw new DomainException("listing.cover_required", "Выберите ровно одну обложку объявления.");
        var publicMedia = media.Where(x => x.Category != VehicleMediaCategory.Documents)
            .OrderBy(x => x.SortOrder).Select(x => new
            {
                x.Id,
                category = x.Category?.ToString(),
                x.IsCover,
                downloadUrl = $"/api/vehicles/{vehicle.Id}/photos/{x.Id}/content/large"
            }).ToArray();
        var snapshot = JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
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

    private async Task<VehicleMediaResponse> PersistMediaAsync(ActorContext actor, Vehicle vehicle, Guid mediaId,
        VehicleMediaCategory? category, string originalFileName, ImageKeys keys, ProcessedVehicleImageSet processed,
        Guid? sourceInspectionPhotoId, bool ownsOriginalObject, string operation, string correlationId,
        CancellationToken cancellationToken)
    {
        var existing = await store.ListMediaAsync(actor.OrganizationId, vehicle.Id, cancellationToken);
        var media = new VehicleMedia(mediaId, actor.OrganizationId, vehicle.BranchId, vehicle.Id, category,
            keys.Original, keys.Thumbnail, keys.Medium, keys.Large, Path.GetFileName(originalFileName),
            processed.Original.ContentType, processed.Original.SizeBytes, processed.Original.Width,
            processed.Original.Height, processed.Thumbnail.Width, processed.Thumbnail.Height,
            processed.Medium.Width, processed.Medium.Height, processed.Large.Width, processed.Large.Height,
            sourceInspectionPhotoId, ownsOriginalObject, existing.Count == 0 ? 10 : existing.Max(x => x.SortOrder) + 10,
            actor.UserId, timeProvider.GetUtcNow());
        await store.AddMediaAsync(media, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, operation, "VehicleMedia", media.Id, null,
            JsonSerializer.Serialize(new
            {
                vehicleId = vehicle.Id,
                category,
                media.OriginalFileName,
                media.SourceInspectionPhotoId
            }), correlationId, timeProvider.GetUtcNow());
        try
        {
            await store.SaveChangesAsync(cancellationToken);
            return Map(media);
        }
        catch
        {
            store.ResetTracking();
            await DeleteOrQueueObjectsAsync(actor.OrganizationId, media.OwnedObjectKeys(), CancellationToken.None);
            var registered = await store.FindMediaRegistrationAsync(mediaId, CancellationToken.None);
            if (registered is not null && registered.OrganizationId == actor.OrganizationId
                && registered.VehicleId == vehicle.Id
                && registered.Media.SourceInspectionPhotoId == sourceInspectionPhotoId) return Map(registered.Media);
            throw;
        }
    }

    private async Task UploadSetAsync(Guid organizationId, ImageKeys keys, ProcessedVehicleImageSet processed,
        CancellationToken cancellationToken)
    {
        var uploaded = new List<string>();
        try
        {
            await PutAsync(keys.Original, processed.Original, cancellationToken); uploaded.Add(keys.Original);
            await PutAsync(keys.Thumbnail, processed.Thumbnail, cancellationToken); uploaded.Add(keys.Thumbnail);
            await PutAsync(keys.Medium, processed.Medium, cancellationToken); uploaded.Add(keys.Medium);
            await PutAsync(keys.Large, processed.Large, cancellationToken); uploaded.Add(keys.Large);
        }
        catch
        {
            await DeleteOrQueueObjectsAsync(organizationId, uploaded, CancellationToken.None);
            throw;
        }
    }

    private async Task UploadDerivativesAsync(Guid organizationId, ImageKeys keys,
        ProcessedVehicleImageSet processed, CancellationToken cancellationToken)
    {
        var uploaded = new List<string>();
        try
        {
            await PutAsync(keys.Thumbnail, processed.Thumbnail, cancellationToken); uploaded.Add(keys.Thumbnail);
            await PutAsync(keys.Medium, processed.Medium, cancellationToken); uploaded.Add(keys.Medium);
            await PutAsync(keys.Large, processed.Large, cancellationToken); uploaded.Add(keys.Large);
        }
        catch
        {
            await DeleteOrQueueObjectsAsync(organizationId, uploaded, CancellationToken.None);
            throw;
        }
    }

    private async Task PutAsync(string objectKey, ProcessedImageVariant image,
        CancellationToken cancellationToken)
    {
        await using var content = new MemoryStream(image.Content, writable: false);
        await storage.PutAsync(objectKey, content, image.SizeBytes, image.ContentType, cancellationToken);
    }

    private async Task DeleteOrQueueObjectsAsync(Guid organizationId, IEnumerable<string> objectKeys,
        CancellationToken cancellationToken)
    {
        var queued = false;
        foreach (var objectKey in objectKeys.Distinct(StringComparer.Ordinal))
        {
            try { await storage.DeleteAsync(objectKey, cancellationToken); }
            catch (StorageUnavailableException)
            {
                await store.QueueObjectDeletionAsync(organizationId, objectKey, timeProvider.GetUtcNow(),
                    CancellationToken.None);
                queued = true;
            }
        }
        if (queued) await store.SaveChangesAsync(CancellationToken.None);
    }

    private async Task DeleteQueuedObjectsAsync(Guid organizationId, IEnumerable<string> objectKeys)
    {
        foreach (var objectKey in objectKeys.Distinct(StringComparer.Ordinal))
        {
            try
            {
                await storage.DeleteAsync(objectKey, CancellationToken.None);
                await store.CompleteObjectDeletionAsync(organizationId, objectKey, CancellationToken.None);
            }
            catch (StorageUnavailableException)
            {
                // Metadata deletion and the tenant-aware cleanup record are already committed.
            }
        }
    }

    private async Task EnsureCapacityAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        if (await store.CountMediaAsync(organizationId, vehicleId, cancellationToken) >= MaxPhotosPerVehicle)
            throw new DomainException("media.limit_reached",
                $"Для автомобиля разрешено не более {MaxPhotosPerVehicle} фотографий.");
    }

    private async Task<Vehicle> FindScopedVehicleAsync(ActorContext actor, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        DemandBranch(actor, vehicle.BranchId);
        return vehicle;
    }

    private async Task<Vehicle> FindReadyVehicleAsync(ActorContext actor, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        var vehicle = await FindScopedVehicleAsync(actor, vehicleId, cancellationToken);
        if (vehicle.Status != VehicleStatus.ReadyForSale)
            throw new DomainException("listing.vehicle_not_ready", "Объявление доступно после успешного QC.");
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
        media.Category?.ToString(), media.OriginalFileName, media.ContentType, media.SizeBytes, media.Width,
        media.Height, media.SortOrder, media.IsCover, media.IsIncludedInListing, media.Caption, media.FocalPointX,
        media.FocalPointY, media.SourceInspectionPhotoId, media.Version,
        $"/api/vehicles/{media.VehicleId}/photos/{media.Id}/content/thumbnail",
        $"/api/vehicles/{media.VehicleId}/photos/{media.Id}/content/medium",
        $"/api/vehicles/{media.VehicleId}/photos/{media.Id}/content/large",
        $"/api/vehicles/{media.VehicleId}/photos/{media.Id}/content/original",
        $"/api/vehicles/{media.VehicleId}/photos/{media.Id}/content/medium", media.CreatedAt);

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

    private sealed record ImageKeys(string Original, string Thumbnail, string Medium, string Large)
    {
        public static ImageKeys Create(string root, string extension, string? original = null) => new(
            original ?? $"{root}/original.{extension}", $"{root}/thumbnail.{extension}",
            $"{root}/medium.{extension}", $"{root}/large.{extension}");
    }
}
