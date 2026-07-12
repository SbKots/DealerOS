using System.Text.Json;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Inspections.Domain;
using DealerOS.Modules.Vehicles.Application;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Inspections.Application;

public sealed class InspectionService(IInspectionStore store, IAuditWriter audit, IInspectionPhotoStorage photoStorage,
    IInspectionImageProcessor imageProcessor, TimeProvider timeProvider)
{
    public Task<IReadOnlyList<InspectionQueueVehicleResponse>> ListQueueAsync(ActorContext actor,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsView);
        return store.ListQueueAsync(actor.OrganizationId, actor.BranchIds, cancellationToken);
    }

    public async Task<IReadOnlyList<InspectionSummaryResponse>> ListForVehicleAsync(ActorContext actor, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsView);
        return await store.ListForVehicleAsync(actor.OrganizationId, vehicleId, actor.BranchIds, cancellationToken);
    }

    public async Task<InspectionDetailResponse> GetAsync(ActorContext actor, Guid inspectionId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsView);
        return await store.GetResponseAsync(actor.OrganizationId, inspectionId, actor.BranchIds, cancellationToken)
            ?? throw new NotFoundException("Осмотр не найден.");
    }

    public async Task<InspectionDetailResponse> StartAsync(ActorContext actor, Guid vehicleId,
        StartInspectionRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsCreate);
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        DemandBranch(actor, vehicle.BranchId);

        var active = await store.FindActiveAsync(actor.OrganizationId, vehicleId, cancellationToken);
        if (active is not null)
            return await GetResponseRequiredAsync(actor, active.Id, cancellationToken);

        var template = request.TemplateId is null
            ? await store.FindLatestTemplateAsync(actor.OrganizationId, cancellationToken)
            : await store.FindTemplateAsync(actor.OrganizationId, request.TemplateId.Value, cancellationToken);
        if (template is null)
            throw new DomainException("inspection.template_required", "Для организации не настроен шаблон осмотра.");

        var now = timeProvider.GetUtcNow();
        var inspection = Inspection.CreateDraft(actor.OrganizationId, vehicle.BranchId, vehicle.Id, actor.UserId,
            template, request.MileageKm, now);
        inspection.Start(now);
        vehicle.BeginInspection(now, actor.UserId);
        await store.AddAsync(inspection, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "inspection.started", "Inspection", inspection.Id, null,
            JsonSerializer.Serialize(new
            {
                inspection.VehicleId,
                inspection.TemplateId,
                inspection.TemplateVersion,
                inspection.MileageKm
            }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetResponseRequiredAsync(actor, inspection.Id, cancellationToken);
    }

    public async Task<InspectionDetailResponse> StartCorrectionAsync(ActorContext actor, Guid completedInspectionId,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsCreate);
        Demand(actor, Permissions.VehicleInspectionsComplete);
        var source = await FindScopedAsync(actor, completedInspectionId, cancellationToken);
        if (source.Status != InspectionStatus.Completed)
            throw new DomainException("inspection.correction_requires_completed", "Корректировка доступна только для завершённого осмотра.");
        var active = await store.FindActiveAsync(actor.OrganizationId, source.VehicleId, cancellationToken);
        if (active is not null) return await GetResponseRequiredAsync(actor, active.Id, cancellationToken);
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, source.VehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        var now = timeProvider.GetUtcNow();
        var correction = Inspection.CreateCorrection(source, actor.UserId, now);
        correction.Start(now);
        vehicle.BeginInspectionCorrection(now, actor.UserId);
        await store.AddAsync(correction, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "inspection.correction_started", "Inspection", correction.Id,
            null, JsonSerializer.Serialize(new { sourceInspectionId = source.Id, correction.Revision }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetResponseRequiredAsync(actor, correction.Id, cancellationToken);
    }

    public async Task<InspectionDetailResponse> SaveItemAsync(ActorContext actor, Guid inspectionId, Guid itemId,
        SaveInspectionItemRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsEdit);
        var inspection = await FindScopedAsync(actor, inspectionId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        inspection.SaveItem(itemId, request.Result, request.Comment, request.ExpectedVersion, now);
        audit.Write(actor.OrganizationId, actor.UserId, "inspection.item_saved", "Inspection", inspection.Id, null,
            JsonSerializer.Serialize(new { itemId, request.Result }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetResponseRequiredAsync(actor, inspection.Id, cancellationToken);
    }

    public async Task<InspectionDetailResponse> AddDefectAsync(ActorContext actor, Guid inspectionId,
        AddDefectRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsEdit);
        var inspection = await FindScopedAsync(actor, inspectionId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var defect = inspection.AddDefect(request.DefectId, request.Category, request.Title, request.Description,
            request.Severity, request.Recommendation, request.EstimatedRepairAmount, request.Currency,
            request.RepairRequired, request.BlocksPublication, request.BlocksTestDrive, request.BlocksSale,
            actor.UserId, request.ExpectedVersion, now);
        audit.Write(actor.OrganizationId, actor.UserId, "inspection.defect_added", "InspectionDefect", defect.Id, null,
            JsonSerializer.Serialize(new
            {
                inspectionId,
                defect.Category,
                defect.Severity,
                defect.RepairRequired,
                defect.BlocksPublication,
                defect.BlocksTestDrive,
                defect.BlocksSale
            }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetResponseRequiredAsync(actor, inspection.Id, cancellationToken);
    }

    public async Task<InspectionDetailResponse> AddPhotoAsync(ActorContext actor, Guid inspectionId, Guid defectId,
        Guid photoId, long expectedVersion, string originalFileName, string? declaredContentType, long declaredLength,
        Stream content, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsEdit);
        var inspection = await FindScopedAsync(actor, inspectionId, cancellationToken);
        inspection.ValidateEditable(expectedVersion);
        var existing = inspection.Defects.SelectMany(x => x.Photos).SingleOrDefault(x => x.Id == photoId);
        if (existing is not null) return await GetResponseRequiredAsync(actor, inspection.Id, cancellationToken);

        var normalized = await imageProcessor.NormalizeAsync(content, declaredLength, originalFileName,
            declaredContentType, cancellationToken);
        await using var normalizedContent = normalized.Content;
        var objectKey = $"organizations/{actor.OrganizationId:N}/inspections/{inspection.Id:N}/defects/{defectId:N}/{photoId:N}.{normalized.Extension}";
        await photoStorage.PutAsync(objectKey, normalized.Content, normalized.SizeBytes, normalized.ContentType, cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            inspection.AddPhoto(defectId, photoId, originalFileName, objectKey, normalized.ContentType,
                normalized.SizeBytes, actor.UserId, expectedVersion, now);
            audit.Write(actor.OrganizationId, actor.UserId, "inspection.photo_added", "InspectionPhoto", photoId, null,
                JsonSerializer.Serialize(new { inspectionId, defectId, normalized.ContentType, normalized.SizeBytes }),
                correlationId, now);
            await store.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await photoStorage.DeleteAsync(objectKey, CancellationToken.None);
            }
            catch (StorageUnavailableException)
            {
                // The adapter logs the orphan cleanup failure; preserve the original command failure.
            }
            throw;
        }

        return await GetResponseRequiredAsync(actor, inspection.Id, cancellationToken);
    }

    public async Task<InspectionDetailResponse> RemoveDraftDefectAsync(ActorContext actor, Guid inspectionId,
        Guid defectId, long expectedVersion, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsEdit);
        var inspection = await FindScopedAsync(actor, inspectionId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var objectKeys = inspection.RemoveDraftDefect(defectId, expectedVersion, now);
        audit.Write(actor.OrganizationId, actor.UserId, "inspection.defect_removed", "InspectionDefect", defectId,
            null, JsonSerializer.Serialize(new { inspectionId }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        foreach (var objectKey in objectKeys) await photoStorage.DeleteAsync(objectKey, cancellationToken);
        return await GetResponseRequiredAsync(actor, inspection.Id, cancellationToken);
    }

    public async Task<InspectionDetailResponse> CompleteAsync(ActorContext actor, Guid inspectionId,
        CompleteInspectionRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsComplete);
        var inspection = await FindScopedAsync(actor, inspectionId, cancellationToken);
        if (inspection.Status == InspectionStatus.Completed)
            return await GetResponseRequiredAsync(actor, inspection.Id, cancellationToken);
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, inspection.VehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        var now = timeProvider.GetUtcNow();
        var needsReconditioning = inspection.Complete(request.FinalComment, request.ExpectedVersion, now);
        vehicle.CompleteInspection(needsReconditioning, now, actor.UserId);
        audit.Write(actor.OrganizationId, actor.UserId, "inspection.completed", "Inspection", inspection.Id, null,
            JsonSerializer.Serialize(new
            {
                inspection.VehicleId,
                needsReconditioning,
                defects = inspection.Defects.Count
            }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetResponseRequiredAsync(actor, inspection.Id, cancellationToken);
    }

    public async Task<InspectionDetailResponse> CancelAsync(ActorContext actor, Guid inspectionId,
        CancelInspectionRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsCancel);
        var inspection = await FindScopedAsync(actor, inspectionId, cancellationToken);
        if (inspection.Status == InspectionStatus.Cancelled)
            return await GetResponseRequiredAsync(actor, inspection.Id, cancellationToken);
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, inspection.VehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        var now = timeProvider.GetUtcNow();
        inspection.Cancel(request.ExpectedVersion, now);
        vehicle.CancelInspection(now, actor.UserId);
        audit.Write(actor.OrganizationId, actor.UserId, "inspection.cancelled", "Inspection", inspection.Id, null,
            JsonSerializer.Serialize(new { inspection.VehicleId }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetResponseRequiredAsync(actor, inspection.Id, cancellationToken);
    }

    public async Task<InspectionPhotoDownload> DownloadPhotoAsync(ActorContext actor, Guid inspectionId,
        Guid defectId, Guid photoId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsView);
        var inspection = await FindScopedAsync(actor, inspectionId, cancellationToken);
        var photo = inspection.Defects.SingleOrDefault(x => x.Id == defectId)?.Photos.SingleOrDefault(x => x.Id == photoId)
            ?? throw new NotFoundException("Фотография не найдена.");
        var stream = await photoStorage.GetAsync(photo.ObjectKey, cancellationToken);
        return new InspectionPhotoDownload(stream, photo.ContentType, photo.OriginalFileName, photo.SizeBytes);
    }

    public Task<IReadOnlyList<InspectionTemplateResponse>> ListTemplatesAsync(ActorContext actor,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsView);
        return store.ListTemplatesAsync(actor.OrganizationId, cancellationToken);
    }

    public async Task<InspectionTemplateResponse> CreateTemplateAsync(ActorContext actor, CreateTemplateRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehicleInspectionsManageTemplates);
        var now = timeProvider.GetUtcNow();
        var version = await store.GetNextTemplateVersionAsync(actor.OrganizationId, cancellationToken);
        var template = new InspectionTemplate(Guid.NewGuid(), actor.OrganizationId, request.Name, version,
            request.Items.Select(x => new InspectionTemplateItemDefinition(x.Key ?? string.Empty, x.Category,
                x.Label ?? string.Empty, x.Description, x.IsRequired, x.SortOrder)), now, actor.UserId);
        await store.AddTemplateAsync(template, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "inspection_template.version_created", "InspectionTemplate",
            template.Id, null, JsonSerializer.Serialize(new
            {
                template.Name,
                template.Version,
                itemCount = template.Items.Count
            }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await store.GetTemplateResponseAsync(actor.OrganizationId, template.Id, cancellationToken)
            ?? throw new InvalidOperationException("Шаблон создан, но не может быть прочитан.");
    }

    private async Task<Inspection> FindScopedAsync(ActorContext actor, Guid inspectionId,
        CancellationToken cancellationToken)
    {
        var inspection = await store.FindAsync(actor.OrganizationId, inspectionId, cancellationToken)
            ?? throw new NotFoundException("Осмотр не найден.");
        DemandBranch(actor, inspection.BranchId);
        return inspection;
    }

    private async Task<InspectionDetailResponse> GetResponseRequiredAsync(ActorContext actor, Guid inspectionId,
        CancellationToken cancellationToken) =>
        await store.GetResponseAsync(actor.OrganizationId, inspectionId, actor.BranchIds, cancellationToken)
        ?? throw new NotFoundException("Осмотр не найден.");

    private static void Demand(ActorContext actor, string permission)
    {
        if (!actor.Permissions.Contains(permission))
            throw new ForbiddenException("Недостаточно прав для операции с осмотром.");
    }

    private static void DemandBranch(ActorContext actor, Guid branchId)
    {
        if (!actor.BranchIds.Contains(branchId)) throw new ForbiddenException("Филиал недоступен пользователю.");
    }
}
