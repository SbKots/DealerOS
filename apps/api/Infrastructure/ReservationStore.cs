using DealerOS.Modules.Reservations.Application;
using DealerOS.Modules.Reservations.Domain;
using DealerOS.Modules.Sales.Domain;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DealerOS.Api.Infrastructure;

public sealed class ReservationStore(DealerOsDbContext dbContext) : IReservationStore
{
    public async Task<ReservationSource?> FindSourceAsync(Guid organizationId, Guid approvedOfferSnapshotId,
        CancellationToken cancellationToken)
    {
        var offer = await dbContext.SalesOffers.Include(x => x.ApprovedSnapshot).SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.ApprovedSnapshot != null
            && x.ApprovedSnapshot.Id == approvedOfferSnapshotId, cancellationToken);
        if (offer?.ApprovedSnapshot is null) return null;
        var vehicle = await dbContext.Vehicles.SingleOrDefaultAsync(x => x.OrganizationId == organizationId
            && x.Id == offer.VehicleId, cancellationToken);
        return vehicle is null ? null : new ReservationSource(offer, offer.ApprovedSnapshot, vehicle);
    }

    public Task<bool> HasNewerApprovedOfferAsync(Guid organizationId, Guid leadId, int revision,
        CancellationToken cancellationToken) => dbContext.SalesOffers.AsNoTracking().AnyAsync(x =>
        x.OrganizationId == organizationId && x.LeadId == leadId && x.Revision > revision
        && x.Status == SalesOfferStatus.Approved, cancellationToken);

    public Task<DateTimeOffset?> FindSnapshotValidUntilAsync(Guid organizationId, Guid approvedOfferSnapshotId,
        CancellationToken cancellationToken) => dbContext.ApprovedOfferSnapshots.AsNoTracking().Where(x =>
        x.OrganizationId == organizationId && x.Id == approvedOfferSnapshotId).Select(x =>
        (DateTimeOffset?)x.ValidUntil).SingleOrDefaultAsync(cancellationToken);

    public Task<Reservation?> FindAsync(Guid organizationId, Guid reservationId,
        CancellationToken cancellationToken) => Query().SingleOrDefaultAsync(x => x.OrganizationId == organizationId
        && x.Id == reservationId, cancellationToken);

    public Task<Reservation?> FindByCreateCommandAsync(Guid organizationId, Guid commandId,
        CancellationToken cancellationToken) => Query().SingleOrDefaultAsync(x =>
        x.OrganizationId == organizationId && x.CreateCommandId == commandId, cancellationToken);

    public async Task<IReadOnlyList<Reservation>> ListAsync(Guid organizationId, IReadOnlySet<Guid> branchIds,
        CancellationToken cancellationToken) => await Query().Where(x => x.OrganizationId == organizationId
        && branchIds.Contains(x.BranchId)).OrderBy(x => x.ExpiresAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Reservation>> ListDueAsync(DateTimeOffset now,
        CancellationToken cancellationToken) => await Query().Where(x =>
        (x.Status == ReservationStatus.PendingDeposit || x.Status == ReservationStatus.Active)
        && x.ExpiresAt <= now).OrderBy(x => x.ExpiresAt).Take(200).ToListAsync(cancellationToken);

    public Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => dbContext.Vehicles.SingleOrDefaultAsync(x =>
        x.OrganizationId == organizationId && x.Id == vehicleId, cancellationToken);

    public Task<string?> FindVehicleNameAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => dbContext.Vehicles.AsNoTracking().Where(x =>
        x.OrganizationId == organizationId && x.Id == vehicleId).Select(x => x.Make + " " + x.Model)
        .SingleOrDefaultAsync(cancellationToken);

    public Task<string?> FindCustomerNameAsync(Guid organizationId, Guid customerId,
        CancellationToken cancellationToken) => dbContext.Customers.AsNoTracking().Where(x =>
        x.OrganizationId == organizationId && x.Id == customerId).Select(x => x.Name)
        .SingleOrDefaultAsync(cancellationToken);

    public Task AddAsync(Reservation reservation, CancellationToken cancellationToken) =>
        dbContext.Reservations.AddAsync(reservation, cancellationToken).AsTask();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("reservation.version_conflict", "Бронь или автомобиль изменены конкурентно.");
        }
        catch (Exception exception) when (FindPostgres(exception)?.SqlState is
                                              PostgresErrorCodes.UniqueViolation or
                                              PostgresErrorCodes.DeadlockDetected or
                                              PostgresErrorCodes.SerializationFailure)
        {
            throw new ConflictException("reservation.constraint_conflict", "Команда конфликтует с активной бронью.");
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("reservation.constraint_conflict", "Команда нарушает ограничения брони.");
        }
    }

    public void ResetTracking() => dbContext.ChangeTracker.Clear();

    private IQueryable<Reservation> Query() => dbContext.Reservations.Include(x => x.History).AsSplitQuery();

    private static PostgresException? FindPostgres(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres;
        return null;
    }
}
