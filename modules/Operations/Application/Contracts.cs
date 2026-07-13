using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Reconditioning.Domain;

namespace DealerOS.Modules.Operations.Application;

public sealed record CreateExecutionRequest(Guid PlanId);
public sealed record StartExecutionRequest(long ExpectedVersion);
public sealed record ScheduleWorkOrderRequest(string? AssigneeName, DateTimeOffset DueAt, long ExpectedVersion);
public sealed record StartWorkOrderRequest(long ExpectedVersion);
public sealed record RecordWorkOrderActualsRequest(decimal LaborHours, decimal LaborAmount, decimal ExternalAmount,
    string? ContractorName, string? InvoiceReference, DateTimeOffset? ContractorDueAt, long ExpectedVersion);
public sealed record AddMaterialMovementRequest(Guid MovementId, MaterialMovementType Type, string? Name,
    decimal Quantity, string? Unit, decimal UnitCost, string? Currency, string? SupplierName, long ExpectedVersion);
public sealed record BlockWorkOrderRequest(string? Reason, long ExpectedVersion);
public sealed record CompleteWorkOrderRequest(string? Comment, long ExpectedVersion);
public sealed record SetContractorSettlementRequest(ContractorSettlementStatus Status, string? Comment,
    long ExpectedVersion);
public sealed record ApproveExecutionOverrunRequest(Guid DecisionId, decimal ApprovedLimitAmount, string? Currency,
    string? Reason, long ExpectedVersion);
public sealed record CompleteExecutionRequest(long ExpectedVersion);

public sealed record MaterialMovementResponse(Guid Id, string Type, string Name, decimal Quantity, string Unit,
    decimal UnitCost, string Currency, string? SupplierName, decimal SignedAmount, DateTimeOffset OccurredAt);
public sealed record WorkOrderResponse(Guid Id, Guid SourcePlanWorkId, Guid SourceDefectId, string Title,
    bool IsMandatory, string ExecutorType, string AssigneeName, DateTimeOffset DueAt, string Status,
    decimal PlannedLaborAmount, decimal PlannedPartsAmount, decimal ActualLaborHours, decimal ActualLaborAmount,
    decimal ActualMaterialAmount, decimal ActualExternalAmount, string Currency, string? ContractorName,
    string? InvoiceReference, DateTimeOffset? ContractorDueAt, string SettlementStatus, string? BlockReason,
    string? CompletionComment, IReadOnlyList<MaterialMovementResponse> Materials);
public sealed record ExecutionOverrunDecisionResponse(Guid Id, Guid ActorUserId, decimal ActualAmount,
    decimal ApprovedLimitAmount, string Currency, string Reason, DateTimeOffset DecidedAt);
public sealed record ExecutionNotificationResponse(Guid Id, Guid WorkOrderId, string Type, DateTimeOffset CreatedAt);
public sealed record ExecutionResponse(Guid Id, Guid VehicleId, Guid BranchId, Guid PlanId, Guid BudgetSnapshotId,
    string Status, decimal PlannedAmount, decimal ApprovedLimitAmount, decimal ActualLaborAmount,
    decimal ActualMaterialAmount, decimal ActualExternalAmount, decimal ActualTotalAmount, decimal VarianceAmount,
    string Currency, long Version, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    IReadOnlyList<WorkOrderResponse> WorkOrders, IReadOnlyList<ExecutionOverrunDecisionResponse> OverrunDecisions,
    IReadOnlyList<ExecutionNotificationResponse> Notifications);

public interface IOperationsStore
{
    Task<ReconditioningPlan?> FindPlanAsync(Guid organizationId, Guid planId, CancellationToken cancellationToken);
    Task<ReconditioningExecution?> FindExecutionAsync(Guid organizationId, Guid executionId,
        CancellationToken cancellationToken);
    Task<ReconditioningExecution?> FindBySnapshotAsync(Guid organizationId, Guid budgetSnapshotId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ReconditioningExecution>> ListForVehicleAsync(Guid organizationId, Guid vehicleId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReconditioningExecution>> ListActiveAsync(CancellationToken cancellationToken);
    Task<bool> RequiresIndependentApprovalAsync(Guid organizationId, CancellationToken cancellationToken);
    Task AddAsync(ReconditioningExecution execution, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    void ResetTracking();
}
