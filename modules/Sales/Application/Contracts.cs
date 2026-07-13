using DealerOS.Modules.Crm.Domain;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Sales.Domain;
using DealerOS.Modules.Vehicles.Domain;

namespace DealerOS.Modules.Sales.Application;

public sealed record CreateVisitRequest(Guid VisitId, Guid LeadId, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    bool IncludesTestDrive);
public sealed record RescheduleVisitRequest(Guid CommandId, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    long ExpectedVersion);
public sealed record VisitCommandRequest(Guid CommandId, long ExpectedVersion);
public sealed record VisitReasonCommandRequest(Guid CommandId, string? Reason, long ExpectedVersion);
public sealed record TestDriveCheckOutRequest(Guid CommandId, bool DriverDocumentsChecked, string? IssueChecklist,
    int OdometerOutKm, string? ConditionOut, long ExpectedVersion);
public sealed record TestDriveCheckInRequest(Guid CommandId, string? ReturnChecklist, int OdometerInKm,
    string? ConditionIn, bool IncidentOccurred, string? IncidentComment, long ExpectedVersion);
public sealed record CompleteVisitRequest(Guid CommandId, string? Result, string? NextAction,
    DateTimeOffset? NextActionDueAt, long ExpectedVersion);
public sealed record VisitHistoryResponse(Guid CommandId, string Operation, DateTimeOffset OccurredAt);
public sealed record VisitResponse(Guid Id, Guid BranchId, Guid CustomerId, string CustomerName, Guid LeadId,
    Guid VehicleId, string VehicleName, Guid ResponsibleUserId, string ResponsibleName, string Status,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool IncludesTestDrive, bool DriverDocumentsChecked,
    string? IssueChecklist, string? ReturnChecklist, int? OdometerOutKm, int? OdometerInKm,
    string? ConditionOut, string? ConditionIn, DateTimeOffset? CheckedOutAt, DateTimeOffset? CheckedInAt,
    bool IncidentOccurred, string? IncidentComment, string? Result, string? NextAction,
    DateTimeOffset? NextActionDueAt, string? ClosureReason, long Version,
    IReadOnlyList<VisitHistoryResponse> History);

public sealed record OfferLineRequest(Guid Id, string? Category, string? Name, decimal Amount, string? Currency);
public sealed record PreviewOfferRequest(Guid LeadId, IReadOnlyList<OfferLineRequest> LineItems,
    decimal DiscountAmount);
public sealed record CreateOfferRequest(Guid OfferId, Guid LeadId, DateTimeOffset ValidUntil,
    IReadOnlyList<OfferLineRequest> LineItems, decimal DiscountAmount);
public sealed record UpdateOfferRequest(Guid CommandId, DateTimeOffset ValidUntil,
    IReadOnlyList<OfferLineRequest> LineItems, decimal DiscountAmount, long ExpectedVersion);
public sealed record SubmitOfferRequest(Guid CommandId, long ExpectedVersion);
public sealed record DecideOfferRequest(Guid DecisionId, OfferDecisionType Decision, string? Reason,
    long ExpectedVersion);
public sealed record CreateOfferRevisionRequest(Guid CommandId, Guid RevisionId, DateTimeOffset ValidUntil,
    long ExpectedVersion);
public sealed record OfferReasonCommandRequest(Guid CommandId, string? Reason, long ExpectedVersion);
public sealed record OfferPreviewResponse(decimal BasePriceAmount, decimal LineItemsAmount,
    decimal DiscountAmount, decimal FinalPriceAmount, decimal CostSnapshotAmount, decimal ExpectedMarginAmount,
    decimal MinimumMarginAmount, string Currency, bool BelowMinimumMargin, bool RequiresManagerApproval,
    decimal AutoApprovalDiscountLimit);
public sealed record OfferLineResponse(Guid Id, string Category, string Name, decimal Amount, string Currency);
public sealed record OfferDecisionResponse(Guid DecisionId, string Decision, string Reason, Guid ActorUserId,
    DateTimeOffset OccurredAt);
public sealed record OfferHistoryResponse(Guid CommandId, string Operation, DateTimeOffset OccurredAt);
public sealed record ApprovedOfferSnapshotResponse(Guid Id, int Revision, decimal BasePriceAmount,
    decimal LineItemsAmount, decimal DiscountAmount, decimal FinalPriceAmount, decimal CostSnapshotAmount,
    decimal ExpectedMarginAmount, decimal MinimumMarginAmount, string Currency, DateTimeOffset ValidUntil,
    string LineItemsJson, Guid ApprovedByUserId, DateTimeOffset ApprovedAt);
public sealed record SalesOfferResponse(Guid Id, Guid BranchId, Guid CustomerId, string CustomerName, Guid LeadId,
    Guid VehicleId, string VehicleName, Guid? RevisesOfferId, int Revision, string Status,
    decimal BasePriceAmount, decimal LineItemsAmount, decimal DiscountAmount, decimal FinalPriceAmount,
    decimal CostSnapshotAmount, decimal ExpectedMarginAmount, decimal MinimumMarginAmount, string Currency,
    DateTimeOffset ValidUntil, bool BelowMinimumMargin, bool RequiresManagerApproval,
    decimal AutoApprovalDiscountLimit, long Version, IReadOnlyList<OfferLineResponse> LineItems,
    IReadOnlyList<OfferDecisionResponse> Decisions, IReadOnlyList<OfferHistoryResponse> History,
    ApprovedOfferSnapshotResponse? ApprovedSnapshot);
public sealed record SalesPolicy(decimal AutoApprovalDiscountLimit, decimal MinimumMarginAmount);

public interface ISalesStore
{
    Task<Lead?> FindLeadAsync(Guid organizationId, Guid leadId, CancellationToken cancellationToken);
    Task<Customer?> FindCustomerAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken);
    Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<ListingContent?> FindReadyListingAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken);
    Task<ReconditioningExecution?> FindCompletedExecutionAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken);
    Task<Visit?> FindVisitAsync(Guid organizationId, Guid visitId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Visit>> ListVisitsAsync(Guid organizationId, IReadOnlySet<Guid> branchIds,
        CancellationToken cancellationToken);
    Task<bool> HasVisitOverlapAsync(Guid organizationId, Guid vehicleId, Guid responsibleUserId,
        DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? excludeVisitId, bool includesTestDrive,
        CancellationToken cancellationToken);
    Task<bool> HasCompletedVisitAsync(Guid organizationId, Guid leadId, CancellationToken cancellationToken);
    Task<bool> IsActiveUserInBranchAsync(Guid organizationId, Guid userId, Guid branchId, string permission,
        CancellationToken cancellationToken);
    Task<string?> FindUserNameAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
    Task AddVisitAsync(Visit visit, CancellationToken cancellationToken);
    Task<SalesOffer?> FindOfferAsync(Guid organizationId, Guid offerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesOffer>> ListOffersAsync(Guid organizationId, IReadOnlySet<Guid> branchIds,
        CancellationToken cancellationToken);
    Task AddOfferAsync(SalesOffer offer, CancellationToken cancellationToken);
    Task<SalesPolicy> GetPolicyAsync(Guid organizationId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    void ResetTracking();
}
