using DealerOS.Modules.Crm.Domain;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Sales.Application;
using DealerOS.Modules.Sales.Domain;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DealerOS.Api.Infrastructure;

public sealed class SalesStore(DealerOsDbContext dbContext) : ISalesStore
{
    public Task<Lead?> FindLeadAsync(Guid organizationId, Guid leadId, CancellationToken cancellationToken) =>
        dbContext.Leads.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId
            && x.Id == leadId, cancellationToken);
    public Task<Customer?> FindCustomerAsync(Guid organizationId, Guid customerId,
        CancellationToken cancellationToken) => dbContext.Customers.AsNoTracking().SingleOrDefaultAsync(
        x => x.OrganizationId == organizationId && x.Id == customerId, cancellationToken);
    public Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => dbContext.Vehicles.AsNoTracking().SingleOrDefaultAsync(
        x => x.OrganizationId == organizationId && x.Id == vehicleId, cancellationToken);
    public Task<ListingContent?> FindReadyListingAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => dbContext.ListingContents.AsNoTracking().Where(x =>
        x.OrganizationId == organizationId && x.VehicleId == vehicleId && x.Status == ListingContentStatus.Ready)
        .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(cancellationToken);
    public Task<ReconditioningExecution?> FindCompletedExecutionAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => ExecutionQuery().Where(x => x.OrganizationId == organizationId
        && x.VehicleId == vehicleId && x.Status == ReconditioningExecutionStatus.Completed)
        .OrderByDescending(x => x.CompletedAt).FirstOrDefaultAsync(cancellationToken);
    public Task<Visit?> FindVisitAsync(Guid organizationId, Guid visitId, CancellationToken cancellationToken) =>
        VisitQuery().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == visitId,
            cancellationToken);
    public async Task<IReadOnlyList<Visit>> ListVisitsAsync(Guid organizationId, IReadOnlySet<Guid> branchIds,
        CancellationToken cancellationToken) => await VisitQuery().Where(x => x.OrganizationId == organizationId
        && branchIds.Contains(x.BranchId)).OrderBy(x => x.StartsAt).ToListAsync(cancellationToken);
    public Task<bool> HasVisitOverlapAsync(Guid organizationId, Guid vehicleId, Guid responsibleUserId,
        DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? excludeVisitId, bool includesTestDrive,
        CancellationToken cancellationToken) => dbContext.Visits.AsNoTracking().AnyAsync(x =>
        x.OrganizationId == organizationId && x.Id != excludeVisitId
        && (x.Status == VisitStatus.Scheduled || x.Status == VisitStatus.Arrived)
        && x.StartsAt < endsAt && startsAt < x.EndsAt
        && (x.ResponsibleUserId == responsibleUserId
            || (includesTestDrive && x.IncludesTestDrive && x.VehicleId == vehicleId)), cancellationToken);
    public Task<bool> HasCompletedVisitAsync(Guid organizationId, Guid leadId,
        CancellationToken cancellationToken) => dbContext.Visits.AsNoTracking().AnyAsync(x =>
        x.OrganizationId == organizationId && x.LeadId == leadId && x.Status == VisitStatus.Completed,
        cancellationToken);
    public async Task<bool> IsActiveUserInBranchAsync(Guid organizationId, Guid userId, Guid branchId,
        string permission, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().Include(x => x.BranchAccess).SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.Id == userId && x.IsActive, cancellationToken);
        return user is not null && user.BranchAccess.Any(x => x.BranchId == branchId)
            && user.PermissionSet.Contains(permission);
    }
    public Task<string?> FindUserNameAsync(Guid organizationId, Guid userId,
        CancellationToken cancellationToken) => dbContext.Users.AsNoTracking().Where(x =>
        x.OrganizationId == organizationId && x.Id == userId).Select(x => x.DisplayName)
        .SingleOrDefaultAsync(cancellationToken);
    public Task AddVisitAsync(Visit visit, CancellationToken cancellationToken) =>
        dbContext.Visits.AddAsync(visit, cancellationToken).AsTask();
    public Task<SalesOffer?> FindOfferAsync(Guid organizationId, Guid offerId,
        CancellationToken cancellationToken) => OfferQuery().SingleOrDefaultAsync(x =>
        x.OrganizationId == organizationId && x.Id == offerId, cancellationToken);
    public async Task<IReadOnlyList<SalesOffer>> ListOffersAsync(Guid organizationId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) => await OfferQuery().Where(x =>
        x.OrganizationId == organizationId && branchIds.Contains(x.BranchId)).OrderByDescending(x => x.CreatedAt)
        .ToListAsync(cancellationToken);
    public Task AddOfferAsync(SalesOffer offer, CancellationToken cancellationToken) =>
        dbContext.SalesOffers.AddAsync(offer, cancellationToken).AsTask();
    public Task<SalesPolicy> GetPolicyAsync(Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.Organizations.AsNoTracking().Where(x => x.Id == organizationId).Select(x => new SalesPolicy(
            x.SalesAutoApprovalDiscountLimit, x.SalesMinimumMarginAmount)).SingleAsync(cancellationToken);
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        { throw new ConflictException("sales.version_conflict", "Запись продаж изменена конкурентно."); }
        catch (Exception exception) when (FindPostgres(exception)?.SqlState is
                                              PostgresErrorCodes.ExclusionViolation or
                                              PostgresErrorCodes.DeadlockDetected)
        { throw new ConflictException("sales.visit_slot_conflict", "Автомобиль или менеджер занят в выбранном слоте."); }
        catch (DbUpdateException)
        { throw new ConflictException("sales.constraint_conflict", "Команда конфликтует с сохранёнными данными продаж."); }
    }
    public void ResetTracking() => dbContext.ChangeTracker.Clear();
    private IQueryable<Visit> VisitQuery() => dbContext.Visits.Include(x => x.History).AsSplitQuery();
    private IQueryable<SalesOffer> OfferQuery() => dbContext.SalesOffers.Include(x => x.LineItems)
        .Include(x => x.History).Include(x => x.Decisions).Include(x => x.ApprovedSnapshot).AsSplitQuery();
    private IQueryable<ReconditioningExecution> ExecutionQuery() => dbContext.ReconditioningExecutions
        .Include(x => x.WorkOrders).ThenInclude(x => x.MaterialMovements).AsSplitQuery();
    private static PostgresException? FindPostgres(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres;
        return null;
    }
}
