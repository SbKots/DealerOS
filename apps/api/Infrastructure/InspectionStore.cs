using DealerOS.Modules.Inspections.Application;
using DealerOS.Modules.Inspections.Domain;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DealerOS.Api.Infrastructure;

public sealed class InspectionStore(DealerOsDbContext dbContext, ILogger<InspectionStore> logger) : IInspectionStore
{
    public Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken) =>
        dbContext.Vehicles.Include(x => x.StatusHistory)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == vehicleId, cancellationToken);

    public Task<Inspection?> FindAsync(Guid organizationId, Guid inspectionId, CancellationToken cancellationToken) =>
        dbContext.Inspections.Include(x => x.Items).Include(x => x.Defects).ThenInclude(x => x.Photos)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == inspectionId, cancellationToken);

    public Task<Inspection?> FindActiveAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken) =>
        dbContext.Inspections.Include(x => x.Items).Include(x => x.Defects).ThenInclude(x => x.Photos)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.VehicleId == vehicleId
                && (x.Status == InspectionStatus.Draft || x.Status == InspectionStatus.InProgress), cancellationToken);

    public Task<InspectionTemplate?> FindTemplateAsync(Guid organizationId, Guid templateId,
        CancellationToken cancellationToken) => dbContext.InspectionTemplates.Include(x => x.Items)
        .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == templateId, cancellationToken);

    public Task<InspectionTemplate?> FindLatestTemplateAsync(Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.InspectionTemplates.Include(x => x.Items).Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);

    public async Task<int> GetNextTemplateVersionAsync(Guid organizationId, CancellationToken cancellationToken) =>
        (await dbContext.InspectionTemplates.Where(x => x.OrganizationId == organizationId)
            .Select(x => (int?)x.Version).MaxAsync(cancellationToken) ?? 0) + 1;

    public async Task AddAsync(Inspection inspection, CancellationToken cancellationToken) =>
        await dbContext.Inspections.AddAsync(inspection, cancellationToken);

    public async Task AddTemplateAsync(InspectionTemplate template, CancellationToken cancellationToken) =>
        await dbContext.InspectionTemplates.AddAsync(template, cancellationToken);

    public async Task<IReadOnlyList<InspectionQueueVehicleResponse>> ListQueueAsync(Guid organizationId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) => await (
        from vehicle in dbContext.Vehicles.AsNoTracking()
        join branch in dbContext.Branches.AsNoTracking() on vehicle.BranchId equals branch.Id
        where vehicle.OrganizationId == organizationId && branchIds.Contains(vehicle.BranchId)
            && vehicle.Status == VehicleStatus.InStock
        orderby vehicle.AcceptedAt, vehicle.CreatedAt
        select new InspectionQueueVehicleResponse(vehicle.Id, vehicle.BranchId, branch.Name, vehicle.Vin,
            vehicle.Make, vehicle.Model, vehicle.Year, vehicle.MileageKm, vehicle.AcceptedAt!.Value))
        .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InspectionSummaryResponse>> ListForVehicleAsync(Guid organizationId, Guid vehicleId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) => await dbContext.Inspections.AsNoTracking()
        .Where(x => x.OrganizationId == organizationId && x.VehicleId == vehicleId && branchIds.Contains(x.BranchId))
        .OrderByDescending(x => x.Revision).ThenByDescending(x => x.CreatedAt)
        .Select(x => new InspectionSummaryResponse(x.Id, x.VehicleId, x.BranchId, x.InspectorId,
            x.Status.ToString(), x.StartedAt, x.CompletedAt, x.MileageKm, x.TemplateName, x.TemplateVersion,
            x.Revision, x.CorrectsInspectionId, x.Defects.Any(d => d.RepairRequired || d.BlocksSale),
            x.Defects.Count, x.Version, x.CreatedAt, x.UpdatedAt)).ToListAsync(cancellationToken);

    public async Task<InspectionDetailResponse?> GetResponseAsync(Guid organizationId, Guid inspectionId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken)
    {
        var inspection = await dbContext.Inspections.AsNoTracking().Include(x => x.Items)
            .Include(x => x.Defects).ThenInclude(x => x.Photos)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == inspectionId
                && branchIds.Contains(x.BranchId), cancellationToken);
        if (inspection is null) return null;
        var branchName = await dbContext.Branches.AsNoTracking().Where(x => x.Id == inspection.BranchId
                && x.OrganizationId == organizationId).Select(x => x.Name).SingleAsync(cancellationToken);
        var inspectorName = await dbContext.Users.AsNoTracking().Where(x => x.Id == inspection.InspectorId
                && x.OrganizationId == organizationId).Select(x => x.DisplayName).SingleAsync(cancellationToken);
        return MapDetail(inspection, branchName, inspectorName);
    }

    public async Task<InspectionTemplateResponse?> GetTemplateResponseAsync(Guid organizationId, Guid templateId,
        CancellationToken cancellationToken)
    {
        var template = await dbContext.InspectionTemplates.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == templateId, cancellationToken);
        return template is null ? null : MapTemplate(template);
    }

    public async Task<IReadOnlyList<InspectionTemplateResponse>> ListTemplatesAsync(Guid organizationId,
        CancellationToken cancellationToken) => (await dbContext.InspectionTemplates.AsNoTracking().Include(x => x.Items)
        .Where(x => x.OrganizationId == organizationId).OrderByDescending(x => x.Version)
        .ToListAsync(cancellationToken)).Select(MapTemplate).ToArray();

    public Task<InspectionPhotoRegistration?> FindPhotoRegistrationAsync(Guid organizationId, Guid photoId,
        CancellationToken cancellationToken) =>
        (from photo in dbContext.InspectionPhotos.AsNoTracking()
         join defect in dbContext.InspectionDefects.AsNoTracking()
             on new { photo.OrganizationId, Id = photo.DefectId }
             equals new { defect.OrganizationId, Id = defect.Id }
         where photo.OrganizationId == organizationId && photo.Id == photoId
         select new InspectionPhotoRegistration(defect.InspectionId, defect.Id))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> StageUnreferencedObjectDeletionsAsync(Guid organizationId,
        Guid removedDefectId, IReadOnlyCollection<string> objectKeys, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var queued = new List<string>();
        foreach (var objectKey in objectKeys.Distinct(StringComparer.Ordinal))
        {
            if (await dbContext.InspectionPhotos.AsNoTracking().AnyAsync(x => x.OrganizationId == organizationId
                    && x.ObjectKey == objectKey && x.DefectId != removedDefectId, cancellationToken))
                continue;
            if (await dbContext.VehicleMedia.AsNoTracking().AnyAsync(x => x.OrganizationId == organizationId
                    && x.ObjectKey == objectKey, cancellationToken))
                continue;
            if (!await dbContext.InspectionObjectDeletions.AnyAsync(x => x.OrganizationId == organizationId
                    && x.ObjectKey == objectKey, cancellationToken))
                dbContext.InspectionObjectDeletions.Add(new InspectionObjectDeletion(organizationId, objectKey, now));
            queued.Add(objectKey);
        }
        return queued;
    }

    public async Task CompleteObjectDeletionAsync(Guid organizationId, string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.InspectionObjectDeletions.Where(x => x.OrganizationId == organizationId
                && x.ObjectKey == objectKey).ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Object {ObjectKey} was deleted but its tenant cleanup record could not be completed", objectKey);
        }
    }

    public void ResetTracking() => dbContext.ChangeTracker.Clear();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("inspection.version_conflict", "Осмотр был изменён другим пользователем.");
        }
        catch (DbUpdateException exception) when (FindPostgres(exception, PostgresErrorCodes.UniqueViolation) is { } postgres)
        {
            throw new ConflictException(postgres.ConstraintName == "ux_inspections_active_vehicle"
                ? "inspection.active_exists" : "database.unique_constraint",
                postgres.ConstraintName == "ux_inspections_active_vehicle"
                    ? "У автомобиля уже есть активный осмотр."
                    : "Запись конфликтует с существующими данными.");
        }
        catch (Exception exception) when (FindPostgres(exception, PostgresErrorCodes.DeadlockDetected,
            PostgresErrorCodes.SerializationFailure) is not null)
        {
            throw new ConflictException("inspection.concurrency_conflict", "Операция конфликтует с параллельным изменением.");
        }
    }

    private static InspectionDetailResponse MapDetail(Inspection inspection, string branchName, string inspectorName) =>
        new(inspection.Id, inspection.VehicleId, inspection.BranchId, branchName, inspection.InspectorId,
            inspectorName, inspection.Status.ToString(), inspection.StartedAt, inspection.CompletedAt,
            inspection.MileageKm, inspection.FinalComment, inspection.TemplateName, inspection.TemplateVersion,
            inspection.Revision, inspection.CorrectsInspectionId, inspection.NeedsReconditioning,
            inspection.Version, inspection.CreatedAt, inspection.UpdatedAt,
            inspection.Items.OrderBy(x => x.SortOrder).Select(x => new InspectionItemResponse(x.Id, x.Key,
                x.Category.ToString(), x.Label, x.Description, x.IsRequired, x.SortOrder, x.Result.ToString(),
                x.Comment, x.UpdatedAt)).ToArray(),
            inspection.Defects.OrderByDescending(x => x.Severity).ThenBy(x => x.CreatedAt)
                .Select(x => new InspectionDefectResponse(x.Id, x.Category.ToString(), x.Title, x.Description,
                    x.Severity.ToString(), x.Recommendation, x.EstimatedRepairAmount, x.Currency,
                    x.RepairRequired, x.BlocksPublication, x.BlocksTestDrive, x.BlocksSale,
                    x.CreatedByUserId, x.CreatedAt, x.Photos.OrderBy(p => p.CreatedAt)
                        .Select(p => new InspectionPhotoResponse(p.Id, p.SourcePhotoId, p.OriginalFileName,
                            p.ContentType, p.SizeBytes, $"/api/inspections/{inspection.Id}/defects/{x.Id}/photos/{p.Id}",
                            p.CreatedByUserId, p.CreatedAt)).ToArray())).ToArray());

    private static InspectionTemplateResponse MapTemplate(InspectionTemplate template) => new(template.Id,
        template.Name, template.Version, template.CreatedAt, template.Items.OrderBy(x => x.SortOrder)
            .Select(x => new InspectionTemplateItemResponse(x.Id, x.Key, x.Category.ToString(), x.Label,
                x.Description, x.IsRequired, x.SortOrder)).ToArray());

    private static PostgresException? FindPostgres(Exception exception, params string[] sqlStates)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && sqlStates.Contains(postgres.SqlState)) return postgres;
        return null;
    }
}
