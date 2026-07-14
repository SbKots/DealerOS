using DealerOS.Modules.Inspections.Domain;
using DealerOS.Modules.Operations.Application;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DealerOS.Api.Infrastructure;

public sealed class QualityListingStore(DealerOsDbContext dbContext) : IQualityListingStore
{
    public Task<ReconditioningExecution?> FindExecutionAsync(Guid organizationId, Guid executionId,
        CancellationToken cancellationToken) => dbContext.ReconditioningExecutions
        .Include(x => x.WorkOrders).ThenInclude(x => x.MaterialMovements).Include(x => x.OverrunDecisions)
        .Include(x => x.Notifications).AsSplitQuery().SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.Id == executionId, cancellationToken);

    public Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => dbContext.Vehicles.Include(x => x.StatusHistory)
        .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == vehicleId, cancellationToken);

    public Task<QualityCheck?> FindQualityCheckAsync(Guid organizationId, Guid qualityCheckId,
        CancellationToken cancellationToken) => dbContext.QualityChecks.Include(x => x.Observations)
        .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == qualityCheckId,
            cancellationToken);

    public async Task<IReadOnlyList<ReconditioningExecution>> ListExecutionsForQualityAsync(Guid organizationId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) => await dbContext.ReconditioningExecutions
        .Include(x => x.WorkOrders).ThenInclude(x => x.MaterialMovements).Include(x => x.OverrunDecisions)
        .Include(x => x.Notifications).AsSplitQuery().Where(x => x.OrganizationId == organizationId
            && branchIds.Contains(x.BranchId) && x.Status == ReconditioningExecutionStatus.Completed)
        .OrderByDescending(x => x.CompletedAt).ToListAsync(cancellationToken);

    public Task<QualityCheck?> FindLatestQualityCheckAsync(Guid organizationId, Guid executionId,
        CancellationToken cancellationToken) => dbContext.QualityChecks.Include(x => x.Observations)
        .Where(x => x.OrganizationId == organizationId && x.ExecutionId == executionId)
        .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<QualityCheck>> ListQualityChecksAsync(Guid organizationId, Guid vehicleId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) => await dbContext.QualityChecks
        .Include(x => x.Observations).Where(x => x.OrganizationId == organizationId && x.VehicleId == vehicleId
            && branchIds.Contains(x.BranchId)).OrderByDescending(x => x.Revision).ToListAsync(cancellationToken);

    public Task AddQualityCheckAsync(QualityCheck qualityCheck, CancellationToken cancellationToken) =>
        dbContext.QualityChecks.AddAsync(qualityCheck, cancellationToken).AsTask();

    public async Task<MediaRegistration?> FindMediaRegistrationAsync(Guid mediaId,
        CancellationToken cancellationToken)
    {
        var media = await dbContext.VehicleMedia.SingleOrDefaultAsync(x => x.Id == mediaId, cancellationToken);
        return media is null ? null : new MediaRegistration(media.OrganizationId, media.VehicleId, media);
    }

    public Task<VehicleMedia?> FindMediaAsync(Guid organizationId, Guid mediaId,
        CancellationToken cancellationToken) => dbContext.VehicleMedia.SingleOrDefaultAsync(
        x => x.OrganizationId == organizationId && x.Id == mediaId, cancellationToken);

    public async Task<IReadOnlyList<VehicleMedia>> ListMediaAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => await dbContext.VehicleMedia.Where(
        x => x.OrganizationId == organizationId && x.VehicleId == vehicleId).OrderBy(x => x.SortOrder)
        .ThenBy(x => x.CreatedAt).ToListAsync(cancellationToken);

    public Task AddMediaAsync(VehicleMedia media, CancellationToken cancellationToken) =>
        dbContext.VehicleMedia.AddAsync(media, cancellationToken).AsTask();

    public Task QueueObjectDeletionAsync(Guid organizationId, string objectKey, DateTimeOffset now,
        CancellationToken cancellationToken) => dbContext.InspectionObjectDeletions
        .AddAsync(new InspectionObjectDeletion(organizationId, objectKey, now), cancellationToken).AsTask();

    public Task<ListingContent?> FindListingAsync(Guid organizationId, Guid listingId,
        CancellationToken cancellationToken) => ListingQuery().SingleOrDefaultAsync(
        x => x.OrganizationId == organizationId && x.Id == listingId, cancellationToken);

    public Task<ListingContent?> FindLatestListingAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => ListingQuery().Where(
        x => x.OrganizationId == organizationId && x.VehicleId == vehicleId).OrderByDescending(x => x.Revision)
        .FirstOrDefaultAsync(cancellationToken);

    public Task AddListingAsync(ListingContent listing, CancellationToken cancellationToken) =>
        dbContext.ListingContents.AddAsync(listing, cancellationToken).AsTask();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        { throw new ConflictException("quality_listing.version_conflict", "Данные изменены конкурентно."); }
        catch (DbUpdateException exception) when (FindPostgres(exception, PostgresErrorCodes.UniqueViolation) is not null)
        { throw new ConflictException("quality_listing.unique_conflict", "Конкурентная команда уже создала запись."); }
    }

    public void ResetTracking() => dbContext.ChangeTracker.Clear();
    private IQueryable<ListingContent> ListingQuery() => dbContext.ListingContents
        .Include(x => x.Publications).Include(x => x.History).AsSplitQuery();
    private static PostgresException? FindPostgres(Exception exception, params string[] sqlStates)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && sqlStates.Contains(postgres.SqlState)) return postgres;
        return null;
    }
}
