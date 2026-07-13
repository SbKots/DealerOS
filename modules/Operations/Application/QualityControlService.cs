using System.Text.Json;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Vehicles.Application;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Operations.Application;

public sealed class QualityControlService(IQualityListingStore store, IAuditWriter audit, TimeProvider timeProvider)
{
    public async Task<QualityCheckResponse> CreateAsync(ActorContext actor, Guid executionId,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.QualityCreate);
        var execution = await FindExecutionAsync(actor, executionId, cancellationToken);
        if (execution.Status != ReconditioningExecutionStatus.Completed)
            throw new DomainException("quality.execution_not_completed", "QC создаётся только после завершения execution.");
        var latest = await store.FindLatestQualityCheckAsync(actor.OrganizationId, executionId, cancellationToken);
        if (latest?.Status == QualityCheckStatus.Draft) return Map(latest);
        if (latest?.Status == QualityCheckStatus.Passed)
            throw new ConflictException("quality.already_passed", "Последняя попытка QC уже успешно завершена.");
        var now = timeProvider.GetUtcNow();
        var checklist = JsonSerializer.Serialize(new
        {
            version = 1,
            capturedAt = now,
            mandatoryWorksCompleted = execution.WorkOrders.Where(x => x.IsMandatory)
                .All(x => x.Status == WorkOrderStatus.Completed),
            blockingDefectsClosed = execution.WorkOrders.All(x => x.Status == WorkOrderStatus.Completed),
            approvedOverrunResolved = execution.ActualTotalAmount <= execution.ApprovedLimitAmount
                || execution.OverrunDecisions.Any(x => x.ApprovedLimitAmount >= execution.ActualTotalAmount)
        });
        var quality = QualityCheck.Create(actor.OrganizationId, execution.BranchId, execution.VehicleId,
            execution.Id, (latest?.Revision ?? 0) + 1, actor.UserId, checklist, now);
        await store.AddQualityCheckAsync(quality, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "quality.created", "QualityCheck", quality.Id, null,
            checklist, correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return Map(quality);
    }

    public async Task<QualityCheckResponse> GetAsync(ActorContext actor, Guid qualityCheckId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.QualityView);
        return Map(await FindQualityAsync(actor, qualityCheckId, cancellationToken));
    }

    public async Task<IReadOnlyList<QualityCheckResponse>> ListAsync(ActorContext actor, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.QualityView);
        return (await store.ListQualityChecksAsync(actor.OrganizationId, vehicleId, actor.BranchIds,
            cancellationToken)).Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<QualityQueueResponse>> ListQueueAsync(ActorContext actor,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.QualityView);
        var result = new List<QualityQueueResponse>();
        foreach (var execution in await store.ListExecutionsForQualityAsync(actor.OrganizationId, actor.BranchIds,
                     cancellationToken))
        {
            var latest = await store.FindLatestQualityCheckAsync(actor.OrganizationId, execution.Id,
                cancellationToken);
            if (latest?.Status == QualityCheckStatus.Passed) continue;
            var vehicle = await store.FindVehicleAsync(actor.OrganizationId, execution.VehicleId, cancellationToken);
            if (vehicle is null) continue;
            result.Add(new QualityQueueResponse(execution.Id, vehicle.Id, vehicle.Vin, vehicle.Make, vehicle.Model,
                execution.CompletedAt!.Value, execution.ActualTotalAmount, execution.Currency, latest?.Id,
                latest?.Status.ToString(), latest?.Revision, execution.WorkOrders.Select(x =>
                    new QualityQueueWorkOrderResponse(x.Id, x.SourceDefectId, x.Title)).ToArray()));
        }
        return result;
    }

    public async Task<QualityCheckResponse> AddObservationAsync(ActorContext actor, Guid qualityCheckId,
        AddQualityObservationRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.QualityDecide);
        var quality = await FindQualityAsync(actor, qualityCheckId, cancellationToken);
        var execution = await FindExecutionAsync(actor, quality.ExecutionId, cancellationToken);
        if (request.WorkOrderId is { } workId && execution.WorkOrders.All(x => x.Id != workId))
            throw new DomainException("quality.work_scope", "Work order не относится к проверяемому execution.");
        if (request.DefectId is { } defectId && execution.WorkOrders.All(x => x.SourceDefectId != defectId))
            throw new DomainException("quality.defect_scope", "Дефект не относится к проверяемому execution.");
        var now = timeProvider.GetUtcNow();
        quality.AddObservation(request.ObservationId, request.Severity, request.WorkOrderId, request.DefectId,
            request.Comment, request.RequiresRework, request.ExpectedVersion, now);
        audit.Write(actor.OrganizationId, actor.UserId, "quality.observation_added", "QualityObservation",
            request.ObservationId, null, JsonSerializer.Serialize(request), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return Map(quality);
    }

    public Task<QualityCheckResponse> PassAsync(ActorContext actor, Guid qualityCheckId,
        QualityDecisionRequest request, string correlationId, CancellationToken cancellationToken) =>
        DecideAsync(actor, qualityCheckId, request, "quality.passed", (quality, execution, vehicle, now) =>
        {
            EnsureIndependent(actor, execution);
            if (execution.Status != ReconditioningExecutionStatus.Completed)
                throw new DomainException("quality.execution_not_completed", "Execution должен быть завершён.");
            quality.Pass(actor.UserId, request.Comment, request.ExpectedVersion, now);
            vehicle.MarkReadyForSale(now, actor.UserId);
        }, correlationId, cancellationToken);

    public Task<QualityCheckResponse> ReworkAsync(ActorContext actor, Guid qualityCheckId,
        QualityDecisionRequest request, string correlationId, CancellationToken cancellationToken) =>
        DecideAsync(actor, qualityCheckId, request, "quality.rework_required", (quality, execution, _, now) =>
        {
            EnsureIndependent(actor, execution);
            if (execution.Status != ReconditioningExecutionStatus.Completed)
                throw new DomainException("quality.execution_not_completed", "Execution должен быть завершён.");
            var workOrderIds = quality.RequireRework(actor.UserId, request.Comment, request.ExpectedVersion, now);
            foreach (var workOrderId in workOrderIds)
                execution.ReturnWorkForRework(workOrderId, request.Comment, execution.Version, now);
        }, correlationId, cancellationToken);

    public Task<QualityCheckResponse> RejectAsync(ActorContext actor, Guid qualityCheckId,
        QualityDecisionRequest request, string correlationId, CancellationToken cancellationToken) =>
        DecideAsync(actor, qualityCheckId, request, "quality.rejected", (quality, execution, _, now) =>
        {
            EnsureIndependent(actor, execution);
            quality.Reject(actor.UserId, request.Comment, request.ExpectedVersion, now);
        }, correlationId, cancellationToken);

    private async Task<QualityCheckResponse> DecideAsync(ActorContext actor, Guid qualityCheckId,
        QualityDecisionRequest request, string operation,
        Action<QualityCheck, ReconditioningExecution, Vehicle, DateTimeOffset> command, string correlationId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.QualityDecide);
        var quality = await FindQualityAsync(actor, qualityCheckId, cancellationToken);
        var execution = await FindExecutionAsync(actor, quality.ExecutionId, cancellationToken);
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, quality.VehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        var now = timeProvider.GetUtcNow();
        command(quality, execution, vehicle, now);
        audit.Write(actor.OrganizationId, actor.UserId, operation, "QualityCheck", quality.Id, null,
            JsonSerializer.Serialize(new { quality.Status, request.Comment }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return Map(quality);
    }

    private async Task<QualityCheck> FindQualityAsync(ActorContext actor, Guid qualityCheckId,
        CancellationToken cancellationToken)
    {
        var quality = await store.FindQualityCheckAsync(actor.OrganizationId, qualityCheckId, cancellationToken)
            ?? throw new NotFoundException("QC не найден.");
        DemandBranch(actor, quality.BranchId); return quality;
    }
    private async Task<ReconditioningExecution> FindExecutionAsync(ActorContext actor, Guid executionId,
        CancellationToken cancellationToken)
    {
        var execution = await store.FindExecutionAsync(actor.OrganizationId, executionId, cancellationToken)
            ?? throw new NotFoundException("Execution не найден.");
        DemandBranch(actor, execution.BranchId); return execution;
    }
    private static void EnsureIndependent(ActorContext actor, ReconditioningExecution execution)
    {
        if (execution.CreatedByUserId == actor.UserId)
            throw new ForbiddenException("Исполнитель подготовки не может выполнить независимый QC своей работы.");
    }
    private static QualityCheckResponse Map(QualityCheck quality) => new(quality.Id, quality.VehicleId,
        quality.ExecutionId, quality.Revision, quality.Status.ToString(), quality.ChecklistSnapshotJson,
        quality.DecisionComment, quality.Version, quality.CreatedAt, quality.DecidedAt,
        quality.Observations.OrderBy(x => x.CreatedAt).Select(x => new QualityObservationResponse(x.Id,
            x.Severity.ToString(), x.WorkOrderId, x.DefectId, x.Comment, x.RequiresRework, x.CreatedAt)).ToArray());
    private static void Demand(ActorContext actor, string permission)
    { if (!actor.Permissions.Contains(permission)) throw new ForbiddenException("Недостаточно прав для контроля качества."); }
    private static void DemandBranch(ActorContext actor, Guid branchId)
    { if (!actor.BranchIds.Contains(branchId)) throw new ForbiddenException("Филиал недоступен пользователю."); }
}
