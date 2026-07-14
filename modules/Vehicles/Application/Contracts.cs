using DealerOS.Modules.Vehicles.Domain;

namespace DealerOS.Modules.Vehicles.Application;

public sealed record ActorContext(Guid UserId, Guid OrganizationId, IReadOnlySet<Guid> BranchIds, IReadOnlySet<string> Permissions);
public sealed record CreateVehicleRequest(Guid BranchId, string? Vin, string? Make, string? Model, int Year, int MileageKm, decimal PlannedPurchaseAmount, string? Currency);
public sealed record VehicleResponse(Guid Id, Guid BranchId, string BranchName, string Vin, string Make, string Model,
    int Year, int MileageKm, decimal PlannedPurchaseAmount, string Currency, string Status, string? StockNumber,
    DateTimeOffset CreatedAt, DateTimeOffset? AcceptedAt, long Version, Guid? CoverPhotoId);

public interface IVehicleStore
{
    Task<bool> VinExistsAsync(Guid organizationId, string vin, CancellationToken cancellationToken);
    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken);
    Task<Vehicle?> FindAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VehicleResponse>> ListAsync(Guid organizationId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task<VehicleResponse?> GetAsync(Guid organizationId, Guid vehicleId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task<string?> GetBranchCodeAsync(Guid organizationId, Guid branchId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAuditWriter
{
    void Write(Guid organizationId, Guid actorUserId, string operation, string entityType, Guid entityId,
        string? oldValue, string newValue, string correlationId, DateTimeOffset occurredAt);
}
