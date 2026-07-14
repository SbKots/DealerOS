using DealerOS.Modules.Reservations.Domain;
using DealerOS.Modules.Sales.Domain;
using DealerOS.Modules.Vehicles.Domain;

namespace DealerOS.Modules.Reservations.Application;

public sealed record CreateReservationRequest(Guid ReservationId, Guid CommandId, Guid ApprovedOfferSnapshotId,
    DateTimeOffset ExpiresAt, bool DepositRequired, decimal DepositAmount, string? Currency);
public sealed record ReservationCommandRequest(Guid CommandId, long ExpectedVersion);
public sealed record ReservationReasonCommandRequest(Guid CommandId, string? Reason, long ExpectedVersion);
public sealed record ExtendReservationRequest(Guid CommandId, DateTimeOffset ExpiresAt, string? Reason,
    long ExpectedVersion);
public sealed record RegisterReservationDepositRequest(Guid CommandId, ReservationDepositStatus Status,
    string? ManualReference, string? Reason, long ExpectedVersion);
public sealed record ReservationHistoryResponse(Guid CommandId, string Operation, DateTimeOffset OccurredAt);
public sealed record ReservationResponse(Guid Id, Guid BranchId, Guid VehicleId, string VehicleName,
    Guid CustomerId, string CustomerName, Guid LeadId, Guid ApprovedOfferSnapshotId, string Status,
    string DepositStatus, decimal DepositAmount, string Currency, string? DepositReference,
    DateTimeOffset CreatedAt, DateTimeOffset StartsAt, DateTimeOffset ExpiresAt, DateTimeOffset? ClosedAt,
    string? ClosureReason, long Version, IReadOnlyList<ReservationHistoryResponse> History);

public sealed record ReservationSource(SalesOffer Offer, ApprovedOfferSnapshot Snapshot, Vehicle Vehicle);

public interface IReservationStore
{
    Task<ReservationSource?> FindSourceAsync(Guid organizationId, Guid approvedOfferSnapshotId,
        CancellationToken cancellationToken);
    Task<bool> HasNewerApprovedOfferAsync(Guid organizationId, Guid leadId, int revision,
        CancellationToken cancellationToken);
    Task<DateTimeOffset?> FindSnapshotValidUntilAsync(Guid organizationId, Guid approvedOfferSnapshotId,
        CancellationToken cancellationToken);
    Task<Reservation?> FindAsync(Guid organizationId, Guid reservationId, CancellationToken cancellationToken);
    Task<Reservation?> FindByCreateCommandAsync(Guid organizationId, Guid commandId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Reservation>> ListAsync(Guid organizationId, IReadOnlySet<Guid> branchIds,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Reservation>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<string?> FindVehicleNameAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<string?> FindCustomerNameAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken);
    Task AddAsync(Reservation reservation, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    void ResetTracking();
}
