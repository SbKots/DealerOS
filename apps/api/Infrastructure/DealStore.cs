using DealerOS.Modules.Deals.Application;
using DealerOS.Modules.Deals.Domain;
using DealerOS.Modules.Operations.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DealerOS.Api.Infrastructure;

public sealed class DealStore(DealerOsDbContext dbContext) : IDealStore
{
    public async Task<DealSource?> FindSourceAsync(Guid organizationId, Guid reservationId,
        CancellationToken cancellationToken)
    {
        var reservation = await dbContext.Reservations.Include(x => x.History).SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.Id == reservationId, cancellationToken);
        if (reservation is null) return null;
        var offer = await dbContext.SalesOffers.Include(x => x.ApprovedSnapshot).SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.ApprovedSnapshot != null
            && x.ApprovedSnapshot.Id == reservation.ApprovedOfferSnapshotId, cancellationToken);
        if (offer?.ApprovedSnapshot is null) return null;
        var vehicle = await dbContext.Vehicles.SingleOrDefaultAsync(x => x.OrganizationId == organizationId
            && x.Id == reservation.VehicleId, cancellationToken);
        var customer = await dbContext.Customers.SingleOrDefaultAsync(x => x.OrganizationId == organizationId
            && x.Id == reservation.CustomerId, cancellationToken);
        return vehicle is null || customer is null ? null
            : new DealSource(reservation, offer, offer.ApprovedSnapshot, vehicle, customer);
    }

    public Task<Deal?> FindAsync(Guid organizationId, Guid dealId, CancellationToken cancellationToken) =>
        Query().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == dealId,
            cancellationToken);

    public Task<Deal?> FindByCreateCommandAsync(Guid organizationId, Guid commandId,
        CancellationToken cancellationToken) => Query().SingleOrDefaultAsync(x =>
        x.OrganizationId == organizationId && x.CreateCommandId == commandId, cancellationToken);

    public async Task<IReadOnlyList<Deal>> ListAsync(Guid organizationId, IReadOnlySet<Guid> branchIds,
        CancellationToken cancellationToken) => await Query().Where(x => x.OrganizationId == organizationId
        && branchIds.Contains(x.BranchId)).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);

    public Task<DealerOS.Modules.Vehicles.Domain.Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => dbContext.Vehicles.SingleOrDefaultAsync(x =>
        x.OrganizationId == organizationId && x.Id == vehicleId, cancellationToken);

    public Task<ListingContent?> FindListingAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => dbContext.ListingContents.Include(x => x.Publications)
        .Include(x => x.History).Where(x => x.OrganizationId == organizationId && x.VehicleId == vehicleId)
        .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(cancellationToken);

    public Task AddAsync(Deal deal, CancellationToken cancellationToken) =>
        dbContext.Deals.AddAsync(deal, cancellationToken).AsTask();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        { throw new ConflictException("deal.version_conflict", "Сделка или связанный объект изменены конкурентно."); }
        catch (Exception exception) when (FindPostgres(exception)?.SqlState is
                                              PostgresErrorCodes.UniqueViolation or
                                              PostgresErrorCodes.DeadlockDetected or
                                              PostgresErrorCodes.SerializationFailure)
        { throw new ConflictException("deal.constraint_conflict", "Команда конфликтует с сохранённой сделкой."); }
        catch (DbUpdateException)
        { throw new ConflictException("deal.constraint_conflict", "Команда нарушает ограничения сделки."); }
    }

    public void ResetTracking() => dbContext.ChangeTracker.Clear();

    private IQueryable<Deal> Query() => dbContext.Deals.Include(x => x.History).Include(x => x.Payments)
        .Include(x => x.Documents).Include(x => x.Handover).AsSplitQuery();

    private static PostgresException? FindPostgres(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres;
        return null;
    }
}
