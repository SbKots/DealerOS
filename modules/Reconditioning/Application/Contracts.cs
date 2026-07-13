using DealerOS.Modules.Inspections.Domain;
using DealerOS.Modules.Reconditioning.Domain;
using DealerOS.Modules.Vehicles.Domain;

namespace DealerOS.Modules.Reconditioning.Application;

public sealed record CreateReconditioningPlanRequest(Guid InspectionId);
public sealed record AddReconditioningWorkRequest(Guid WorkId, Guid SourceDefectId, string? Title,
    string? Description, ReconditioningWorkCategory Category, ReconditioningWorkPriority Priority,
    bool IsMandatory, ReconditioningExecutorType ExecutorType, string? ExecutorName,
    decimal EstimatedLaborAmount, decimal EstimatedPartsAmount, string? Currency,
    int EstimatedDurationDays, string? Comment, long ExpectedVersion);
public sealed record UpdateReconditioningWorkRequest(string? Title, string? Description,
    ReconditioningWorkCategory Category, ReconditioningWorkPriority Priority, bool IsMandatory,
    ReconditioningExecutorType ExecutorType, string? ExecutorName, decimal EstimatedLaborAmount,
    decimal EstimatedPartsAmount, string? Currency, int EstimatedDurationDays, string? Comment,
    long ExpectedVersion);
public sealed record RemoveReconditioningWorkRequest(string? OmissionReason, long ExpectedVersion);
public sealed record SubmitReconditioningPlanRequest(long ExpectedVersion);
public sealed record ApproveReconditioningPlanRequest(Guid DecisionId, decimal? ApprovedLimitAmount,
    string? Currency, string? Comment, long ExpectedVersion);
public sealed record RejectReconditioningPlanRequest(Guid DecisionId, string? Reason, long ExpectedVersion);
public sealed record RequestReconditioningChangesRequest(Guid DecisionId, string? Reason, long ExpectedVersion);
public sealed record CancelReconditioningPlanRequest(string? Reason, long ExpectedVersion);
public sealed record CreateReconditioningRevisionRequest(long ExpectedVersion);

public sealed record ReconditioningQueueVehicleResponse(Guid VehicleId, Guid BranchId, string BranchName,
    string Vin, string Make, string Model, int Year, string? StockNumber, Guid InspectionId,
    DateTimeOffset InspectionCompletedAt, int MandatoryDefectCount, bool HasActivePlan,
    Guid? LatestPlanId, string? LatestPlanStatus, int? LatestPlanRevision);
public sealed record ReconditioningBudgetResponse(string Currency, decimal LaborAmount, decimal PartsAmount,
    decimal TotalAmount);
public sealed record ReconditioningWorkResponse(Guid Id, Guid SourceDefectId, string SourceDefectTitle,
    string SourceDefectDescription, string SourceDefectSeverity, string Title, string Description,
    string Category, string Priority, bool IsMandatory, string ExecutorType, string ExecutorName,
    decimal EstimatedLaborAmount, decimal EstimatedPartsAmount, string Currency, int EstimatedDurationDays,
    string? Comment, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ReconditioningOmissionResponse(Guid Id, Guid SourceDefectId, string SourceDefectTitle,
    string Reason, Guid DecidedByUserId, string DecidedByName, DateTimeOffset DecidedAt);
public sealed record ReconditioningDecisionResponse(Guid Id, string Type, Guid ActorUserId, string ActorName,
    string? Reason, decimal? ApprovedLimitAmount, string? Currency, DateTimeOffset DecidedAt);
public sealed record ReconditioningBudgetSnapshotResponse(Guid Id, decimal LaborAmount, decimal PartsAmount,
    decimal PlannedTotalAmount, decimal ApprovedLimitAmount, string Currency, Guid ApprovedByUserId,
    string ApprovedByName, DateTimeOffset ApprovedAt);
public sealed record ReconditioningHistoryResponse(Guid Id, string? FromStatus, string ToStatus,
    Guid ActorUserId, string ActorName, string? Reason, Guid? DecisionId, DateTimeOffset OccurredAt);
public sealed record ReconditioningPlanSummaryResponse(Guid Id, Guid VehicleId, Guid BranchId, string BranchName,
    string Vin, string Make, string Model, string Status, int Revision, Guid SourceInspectionId,
    Guid? RevisesPlanId, Guid CreatedByUserId, string CreatedByName, int WorkCount,
    IReadOnlyList<ReconditioningBudgetResponse> Budgets, long Version, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
public sealed record ReconditioningPlanDetailResponse(Guid Id, Guid VehicleId, Guid BranchId, string BranchName,
    string Vin, string Make, string Model, string? StockNumber, Guid SourceInspectionId,
    Guid CreatedByUserId, string CreatedByName, string Status, int Revision, Guid? RevisesPlanId,
    DateTimeOffset? SubmittedAt, DateTimeOffset? DecidedAt, DateTimeOffset? CancelledAt, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<ReconditioningBudgetResponse> Budgets,
    IReadOnlyList<ReconditioningWorkResponse> Works, IReadOnlyList<ReconditioningOmissionResponse> Omissions,
    IReadOnlyList<ReconditioningDecisionResponse> Decisions,
    IReadOnlyList<ReconditioningBudgetSnapshotResponse> ApprovedBudgetSnapshots,
    IReadOnlyList<ReconditioningHistoryResponse> History);

public interface IReconditioningStore
{
    Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<Inspection?> FindInspectionAsync(Guid organizationId, Guid inspectionId, CancellationToken cancellationToken);
    Task<ReconditioningPlan?> FindAsync(Guid organizationId, Guid planId, CancellationToken cancellationToken);
    Task<ReconditioningPlan?> FindActiveAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<ReconditioningPlan?> FindLatestAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<bool> RequiresIndependentApprovalAsync(Guid organizationId, CancellationToken cancellationToken);
    Task AddAsync(ReconditioningPlan plan, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReconditioningQueueVehicleResponse>> ListRequiredVehiclesAsync(Guid organizationId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReconditioningPlanSummaryResponse>> ListApprovalsAsync(Guid organizationId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReconditioningPlanSummaryResponse>> ListForVehicleAsync(Guid organizationId, Guid vehicleId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task<ReconditioningPlanDetailResponse?> GetResponseAsync(Guid organizationId, Guid planId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    void ResetTracking();
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
