using DealerOS.Modules.Crm.Domain;
using DealerOS.Modules.Deals.Domain;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Reservations.Domain;
using DealerOS.Modules.Sales.Domain;
using DealerOS.Modules.Vehicles.Domain;

namespace DealerOS.Modules.Deals.Application;

public sealed record CreateDealRequest(Guid DealId, Guid CommandId, Guid ReservationId);
public sealed record DealCommandRequest(Guid CommandId, long ExpectedVersion);
public sealed record DealReasonCommandRequest(Guid CommandId, string? Reason, long ExpectedVersion);
public sealed record RegisterDealPaymentRequest(Guid PaymentId, Guid CommandId, PaymentKind Kind,
    PaymentStatus Status, decimal Amount, string? Currency, string? ManualReference, string? Reason,
    DateTimeOffset OccurredAt, long ExpectedVersion);
public sealed record GenerateDealDocumentRequest(Guid DocumentId, Guid CommandId, DealDocumentType Type,
    string? Reason, long ExpectedVersion);
public sealed record CompleteHandoverRequest(Guid CommandId, int ActualMileageKm, bool KeysTransferred,
    bool DocumentsTransferred, bool EquipmentTransferred, bool ConditionConfirmed, bool IssuerConfirmed,
    bool ResponsibleConfirmed, string? ConditionNotes, string? Comments, long ExpectedVersion);
public sealed record DealPaymentResponse(Guid Id, Guid CommandId, string Kind, string Status, decimal Amount,
    string Currency, string ManualReference, string Reason, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt);
public sealed record DealDocumentResponse(Guid Id, Guid CommandId, string Type, string Number,
    string TemplateName, int TemplateVersion, int Revision, Guid? SourceDocumentId, string Sha256, long SizeBytes,
    string? Reason, DateTimeOffset GeneratedAt);
public sealed record DealHandoverResponse(int ActualMileageKm, bool KeysTransferred, bool DocumentsTransferred,
    bool EquipmentTransferred, bool ConditionConfirmed, bool IssuerConfirmed, bool ResponsibleConfirmed,
    string ConditionNotes, string? Comments, DateTimeOffset CompletedAt);
public sealed record DealHistoryResponse(Guid CommandId, string Operation, DateTimeOffset OccurredAt);
public sealed record DealResponse(Guid Id, Guid BranchId, Guid ReservationId, Guid ApprovedOfferSnapshotId,
    Guid CustomerId, Guid LeadId, Guid VehicleId, string CustomerName, string VehicleSnapshotJson,
    string LineItemsJson, string Status, decimal BasePriceAmount, decimal LineItemsAmount, decimal DiscountAmount,
    decimal FinalTotalAmount, decimal CostSnapshotAmount, decimal ExpectedMarginAmount, string Currency,
    decimal ReceivedTotal, decimal RefundedTotal, decimal NetPaid, decimal Balance, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, DateTimeOffset? ClosedAt, string? ClosureReason, long Version,
    IReadOnlyList<DealPaymentResponse> Payments, IReadOnlyList<DealDocumentResponse> Documents,
    DealHandoverResponse? Handover, IReadOnlyList<DealHistoryResponse> History);
public sealed record DealDocumentDownload(Stream Content, string FileName, string ContentType);
public sealed record DealPdfModel(DealDocumentType Type, string DocumentNumber, int TemplateVersion,
    Guid DealId, string CustomerName, string VehicleSnapshotJson, string LineItemsJson, decimal FinalTotalAmount,
    string Currency, DateTimeOffset GeneratedAt);

public sealed record DealSource(Reservation Reservation, SalesOffer Offer, ApprovedOfferSnapshot Snapshot,
    Vehicle Vehicle, Customer Customer);

public interface IDealStore
{
    Task<DealSource?> FindSourceAsync(Guid organizationId, Guid reservationId, CancellationToken cancellationToken);
    Task<Deal?> FindAsync(Guid organizationId, Guid dealId, CancellationToken cancellationToken);
    Task<Deal?> FindByCreateCommandAsync(Guid organizationId, Guid commandId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Deal>> ListAsync(Guid organizationId, IReadOnlySet<Guid> branchIds,
        CancellationToken cancellationToken);
    Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<ListingContent?> FindListingAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task AddAsync(Deal deal, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    void ResetTracking();
}

public interface IDealDocumentStorage
{
    Task PutAsync(string objectKey, Stream content, long sizeBytes, CancellationToken cancellationToken);
    Task<Stream> GetAsync(string objectKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public interface IDealPdfGenerator
{
    byte[] Generate(DealPdfModel model);
}
