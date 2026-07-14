using DealerOS.Modules.Vehicles.Application;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DealerOS.Api.Infrastructure;

public sealed class VehicleStore(DealerOsDbContext dbContext) : IVehicleStore, IAuditWriter
{
    public Task<bool> VinExistsAsync(Guid organizationId, string vin, CancellationToken cancellationToken) =>
        dbContext.Vehicles.AnyAsync(x => x.OrganizationId == organizationId && x.Vin == vin, cancellationToken);

    public async Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken) =>
        await dbContext.Vehicles.AddAsync(vehicle, cancellationToken);

    public Task<Vehicle?> FindAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken) =>
        dbContext.Vehicles.Include(x => x.StatusHistory)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == vehicleId, cancellationToken);

    public async Task<IReadOnlyList<VehicleResponse>> ListAsync(Guid organizationId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) =>
        await Project(organizationId, branchIds, null).ToListAsync(cancellationToken);

    public Task<VehicleResponse?> GetAsync(Guid organizationId, Guid vehicleId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) =>
        Project(organizationId, branchIds, vehicleId).SingleOrDefaultAsync(cancellationToken);

    public Task<string?> GetBranchCodeAsync(Guid organizationId, Guid branchId, CancellationToken cancellationToken) =>
        dbContext.Branches.Where(x => x.OrganizationId == organizationId && x.Id == branchId)
            .Select(x => x.Code).SingleOrDefaultAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            throw new ConflictException("database.unique_constraint", postgres.ConstraintName switch
            {
                "ux_vehicles_organization_vin" => "Автомобиль с таким VIN уже существует в организации.",
                "ux_vehicles_organization_stock_number" => "Складской номер уже используется.",
                _ => "Запись конфликтует с уже существующими данными."
            });
        }
        catch (Exception exception) when (IsPostgresConcurrencyConflict(exception))
        {
            throw new ConflictException("database.concurrency_conflict",
                "Данные были одновременно изменены другим запросом. Обновите страницу.");
        }
    }

    public void Write(Guid organizationId, Guid actorUserId, string operation, string entityType, Guid entityId,
        string? oldValue, string newValue, string correlationId, DateTimeOffset occurredAt) =>
        dbContext.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), organizationId, actorUserId, operation, entityType,
            entityId, oldValue, newValue, correlationId, occurredAt));

    private IQueryable<VehicleResponse> Project(Guid organizationId, IReadOnlySet<Guid> branchIds, Guid? vehicleId) =>
        from vehicle in dbContext.Vehicles.AsNoTracking()
        join branch in dbContext.Branches.AsNoTracking() on vehicle.BranchId equals branch.Id
        where vehicle.OrganizationId == organizationId && branchIds.Contains(vehicle.BranchId)
            && (vehicleId == null || vehicle.Id == vehicleId)
        orderby vehicle.CreatedAt descending
        select new VehicleResponse(vehicle.Id, vehicle.BranchId, branch.Name, vehicle.Vin, vehicle.Make, vehicle.Model,
            vehicle.Year, vehicle.MileageKm, vehicle.PlannedPurchaseAmount, vehicle.Currency, vehicle.Status.ToString(),
            vehicle.StockNumber, vehicle.CreatedAt, vehicle.AcceptedAt, vehicle.Version,
            dbContext.VehicleMedia.Where(media => media.OrganizationId == organizationId
                && media.VehicleId == vehicle.Id && media.IsCover).Select(media => (Guid?)media.Id).SingleOrDefault());

    private static bool IsPostgresConcurrencyConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException
                {
                    SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure
                })
            {
                return true;
            }
        }

        return false;
    }
}
