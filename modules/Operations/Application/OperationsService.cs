using System.Text.Json;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Reconditioning.Domain;
using DealerOS.Modules.Vehicles.Application;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Operations.Application;

public sealed class OperationsService(IOperationsStore store, IAuditWriter audit, TimeProvider timeProvider)
{
    public async Task<ExecutionResponse> CreateAsync(ActorContext actor, CreateExecutionRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.OperationsCreate);
        var plan = await store.FindPlanAsync(actor.OrganizationId, request.PlanId, cancellationToken)
            ?? throw new NotFoundException("Утверждённый план не найден.");
        DemandBranch(actor, plan.BranchId);
        if (plan.Status != ReconditioningPlanStatus.Approved)
            throw new DomainException("operations.plan_not_approved", "Execution создаётся только из утверждённого плана.");
        var snapshot = plan.BudgetSnapshots.SingleOrDefault()
            ?? throw new DomainException("operations.snapshot_missing", "У утверждённого плана отсутствует budget snapshot.");
        var existing = await store.FindBySnapshotAsync(actor.OrganizationId, snapshot.Id, cancellationToken);
        if (existing is not null) return Map(existing);

        var now = timeProvider.GetUtcNow();
        var execution = ReconditioningExecution.Create(actor.OrganizationId, plan.BranchId, plan.VehicleId,
            plan.Id, snapshot.Id, actor.UserId, snapshot.PlannedTotalAmount, snapshot.ApprovedLimitAmount,
            snapshot.Currency, plan.Works.Select(x => new ExecutionSourceWork(x.Id, x.SourceDefectId, x.Title,
                x.IsMandatory, x.ExecutorType.ToString(), x.ExecutorName, x.EstimatedLaborAmount,
                x.EstimatedPartsAmount, x.Currency, x.EstimatedDurationDays)), now);
        await store.AddAsync(execution, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "operations.execution_created", "ReconditioningExecution",
            execution.Id, null, JsonSerializer.Serialize(new
            {
                planId = plan.Id,
                budgetSnapshotId = snapshot.Id,
                execution.VehicleId
            }),
            correlationId, now);
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException exception) when (exception.Code == "operations.execution_exists")
        {
            store.ResetTracking();
            existing = await store.FindBySnapshotAsync(actor.OrganizationId, snapshot.Id, CancellationToken.None);
            if (existing is not null) return Map(existing);
            throw;
        }
        return Map(execution);
    }

    public async Task<ExecutionResponse> GetAsync(ActorContext actor, Guid executionId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.OperationsView);
        return Map(await FindScopedAsync(actor, executionId, cancellationToken));
    }

    public async Task<IReadOnlyList<ExecutionResponse>> ListForVehicleAsync(ActorContext actor, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.OperationsView);
        var executions = await store.ListForVehicleAsync(actor.OrganizationId, vehicleId, actor.BranchIds,
            cancellationToken);
        return [.. executions.Select(Map)];
    }

    public Task<ExecutionResponse> StartAsync(ActorContext actor, Guid executionId, StartExecutionRequest request,
        string correlationId, CancellationToken cancellationToken) => MutateAsync(actor, executionId,
        Permissions.OperationsEdit, "operations.execution_started", request.ExpectedVersion,
        (execution, now) => execution.Start(actor.UserId, request.ExpectedVersion, now), correlationId,
        cancellationToken);

    public Task<ExecutionResponse> ScheduleWorkAsync(ActorContext actor, Guid executionId, Guid workOrderId,
        ScheduleWorkOrderRequest request, string correlationId, CancellationToken cancellationToken) =>
        MutateAsync(actor, executionId, Permissions.OperationsEdit, "operations.work_scheduled",
            request.ExpectedVersion, (execution, now) => execution.ScheduleWork(workOrderId, request.AssigneeName,
                request.DueAt, request.ExpectedVersion, now), correlationId, cancellationToken, workOrderId);

    public Task<ExecutionResponse> StartWorkAsync(ActorContext actor, Guid executionId, Guid workOrderId,
        StartWorkOrderRequest request, string correlationId, CancellationToken cancellationToken) =>
        MutateAsync(actor, executionId, Permissions.OperationsEdit, "operations.work_started",
            request.ExpectedVersion, (execution, now) => execution.StartWork(workOrderId, request.ExpectedVersion,
                now), correlationId, cancellationToken, workOrderId);

    public Task<ExecutionResponse> RecordActualsAsync(ActorContext actor, Guid executionId, Guid workOrderId,
        RecordWorkOrderActualsRequest request, string correlationId, CancellationToken cancellationToken) =>
        MutateAsync(actor, executionId, Permissions.OperationsEdit, "operations.actuals_recorded",
            request.ExpectedVersion, (execution, now) => execution.RecordActuals(workOrderId, request.LaborHours,
                request.LaborAmount, request.ExternalAmount, request.ContractorName, request.InvoiceReference,
                request.ContractorDueAt, request.ExpectedVersion, now), correlationId, cancellationToken,
            workOrderId);

    public async Task<ExecutionResponse> AddMaterialAsync(ActorContext actor, Guid executionId, Guid workOrderId,
        AddMaterialMovementRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.OperationsEdit);
        var execution = await FindScopedAsync(actor, executionId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var changed = execution.AddMaterial(workOrderId, request.MovementId, request.Type, request.Name,
            request.Quantity, request.Unit, request.UnitCost, request.Currency, request.SupplierName, actor.UserId,
            request.ExpectedVersion, now);
        if (!changed) return Map(execution);
        audit.Write(actor.OrganizationId, actor.UserId, "operations.material_recorded", "MaterialMovement",
            request.MovementId, null, JsonSerializer.Serialize(new
            {
                executionId,
                workOrderId,
                request.Name,
                request.Quantity,
                request.Unit,
                request.UnitCost,
                request.Currency
            }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return Map(execution);
    }

    public Task<ExecutionResponse> BlockWorkAsync(ActorContext actor, Guid executionId, Guid workOrderId,
        BlockWorkOrderRequest request, string correlationId, CancellationToken cancellationToken) =>
        MutateAsync(actor, executionId, Permissions.OperationsEdit, "operations.work_blocked",
            request.ExpectedVersion, (execution, now) => execution.BlockWork(workOrderId, request.Reason,
                request.ExpectedVersion, now), correlationId, cancellationToken, workOrderId);

    public Task<ExecutionResponse> ResumeWorkAsync(ActorContext actor, Guid executionId, Guid workOrderId,
        StartWorkOrderRequest request, string correlationId, CancellationToken cancellationToken) =>
        MutateAsync(actor, executionId, Permissions.OperationsEdit, "operations.work_resumed",
            request.ExpectedVersion, (execution, now) => execution.ResumeWork(workOrderId, request.ExpectedVersion,
                now), correlationId, cancellationToken, workOrderId);

    public Task<ExecutionResponse> CompleteWorkAsync(ActorContext actor, Guid executionId, Guid workOrderId,
        CompleteWorkOrderRequest request, string correlationId, CancellationToken cancellationToken) =>
        MutateAsync(actor, executionId, Permissions.OperationsComplete, "operations.work_completed",
            request.ExpectedVersion, (execution, now) => execution.CompleteWork(workOrderId, request.Comment,
                request.ExpectedVersion, now), correlationId, cancellationToken, workOrderId);

    public Task<ExecutionResponse> SetSettlementAsync(ActorContext actor, Guid executionId, Guid workOrderId,
        SetContractorSettlementRequest request, string correlationId, CancellationToken cancellationToken) =>
        MutateAsync(actor, executionId, Permissions.OperationsManageSettlement, "operations.settlement_changed",
            request.ExpectedVersion, (execution, now) => execution.SetSettlement(workOrderId, request.Status,
                request.Comment, actor.UserId, request.ExpectedVersion, now), correlationId, cancellationToken,
            workOrderId);

    public async Task<ExecutionResponse> ApproveOverrunAsync(ActorContext actor, Guid executionId,
        ApproveExecutionOverrunRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.OperationsApproveOverrun);
        var execution = await FindScopedAsync(actor, executionId, cancellationToken);
        if (await store.RequiresIndependentApprovalAsync(actor.OrganizationId, cancellationToken)
            && execution.CreatedByUserId == actor.UserId)
            throw new ForbiddenException("Автор execution не может согласовать собственный перерасход.");
        var now = timeProvider.GetUtcNow();
        var changed = execution.ApproveOverrun(request.DecisionId, actor.UserId, request.ApprovedLimitAmount,
            request.Currency, request.Reason, request.ExpectedVersion, now);
        if (!changed) return Map(execution);
        audit.Write(actor.OrganizationId, actor.UserId, "operations.overrun_approved", "ExecutionOverrunDecision",
            request.DecisionId, null, JsonSerializer.Serialize(new
            {
                executionId,
                request.ApprovedLimitAmount,
                request.Currency,
                request.Reason
            }), correlationId, now);
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            store.ResetTracking();
            var current = await FindScopedAsync(actor, executionId, CancellationToken.None);
            if (!current.ApproveOverrun(request.DecisionId, actor.UserId, request.ApprovedLimitAmount,
                    request.Currency, request.Reason, current.Version, now)) return Map(current);
            throw;
        }
        return Map(execution);
    }

    public Task<ExecutionResponse> CompleteAsync(ActorContext actor, Guid executionId,
        CompleteExecutionRequest request, string correlationId, CancellationToken cancellationToken) =>
        MutateAsync(actor, executionId, Permissions.OperationsComplete, "operations.execution_completed",
            request.ExpectedVersion, (execution, now) => execution.Complete(actor.UserId, request.ExpectedVersion,
                now), correlationId, cancellationToken);

    public async Task<int> GenerateNotificationsAsync(ActorContext actor, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.OperationsEdit);
        var executions = await store.ListActiveAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var count = 0;
        foreach (var execution in executions.Where(x => x.OrganizationId == actor.OrganizationId
                     && actor.BranchIds.Contains(x.BranchId)))
            count += execution.GenerateDeadlineNotifications(now, TimeSpan.FromDays(1));
        if (count > 0) await store.SaveChangesAsync(cancellationToken);
        return count;
    }

    private async Task<ExecutionResponse> MutateAsync(ActorContext actor, Guid executionId, string permission,
        string operation, long expectedVersion, Action<ReconditioningExecution, DateTimeOffset> command,
        string correlationId, CancellationToken cancellationToken, Guid? entityId = null)
    {
        Demand(actor, permission);
        var execution = await FindScopedAsync(actor, executionId, cancellationToken);
        var before = JsonSerializer.Serialize(new
        {
            execution.Status,
            execution.Version,
            execution.ActualTotalAmount
        });
        var now = timeProvider.GetUtcNow();
        command(execution, now);
        audit.Write(actor.OrganizationId, actor.UserId, operation, entityId is null ? "ReconditioningExecution" : "WorkOrder",
            entityId ?? execution.Id, before, JsonSerializer.Serialize(new
            {
                execution.Status,
                execution.Version,
                execution.ActualTotalAmount
            }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return Map(execution);
    }

    private async Task<ReconditioningExecution> FindScopedAsync(ActorContext actor, Guid executionId,
        CancellationToken cancellationToken)
    {
        var execution = await store.FindExecutionAsync(actor.OrganizationId, executionId, cancellationToken)
            ?? throw new NotFoundException("Execution не найден.");
        DemandBranch(actor, execution.BranchId);
        return execution;
    }

    private static ExecutionResponse Map(ReconditioningExecution execution) => new(execution.Id,
        execution.VehicleId, execution.BranchId, execution.PlanId, execution.BudgetSnapshotId,
        execution.Status.ToString(), execution.PlannedAmount, execution.ApprovedLimitAmount,
        execution.ActualLaborAmount, execution.ActualMaterialAmount, execution.ActualExternalAmount,
        execution.ActualTotalAmount, execution.VarianceAmount, execution.Currency, execution.Version,
        execution.StartedAt, execution.CompletedAt, [.. execution.WorkOrders.OrderBy(x => x.CreatedAt).Select(x =>
            new WorkOrderResponse(x.Id, x.SourcePlanWorkId, x.SourceDefectId, x.Title, x.IsMandatory,
                x.ExecutorType, x.AssigneeName, x.DueAt, x.Status.ToString(), x.PlannedLaborAmount,
                x.PlannedPartsAmount, x.ActualLaborHours, x.ActualLaborAmount, x.MaterialAmount,
                x.ActualExternalAmount, x.Currency, x.ContractorName, x.InvoiceReference, x.ContractorDueAt,
                x.SettlementStatus.ToString(), x.BlockReason, x.CompletionComment,
                [.. x.MaterialMovements.OrderBy(m => m.OccurredAt).Select(m => new MaterialMovementResponse(m.Id,
                    m.Type.ToString(), m.Name, m.Quantity, m.Unit, m.UnitCost, m.Currency, m.SupplierName,
                    m.SignedAmount, m.OccurredAt))]))],
        [.. execution.OverrunDecisions.OrderBy(x => x.DecidedAt).Select(x => new ExecutionOverrunDecisionResponse(x.Id,
            x.ActorUserId, x.ActualAmount, x.ApprovedLimitAmount, x.Currency, x.Reason, x.DecidedAt))],
        [.. execution.Notifications.OrderByDescending(x => x.CreatedAt).Select(x => new ExecutionNotificationResponse(
            x.Id, x.WorkOrderId, x.Type.ToString(), x.CreatedAt))]);

    private static void Demand(ActorContext actor, string permission)
    {
        if (!actor.Permissions.Contains(permission))
            throw new ForbiddenException("Недостаточно прав для операции выполнения подготовки.");
    }

    private static void DemandBranch(ActorContext actor, Guid branchId)
    {
        if (!actor.BranchIds.Contains(branchId)) throw new ForbiddenException("Филиал недоступен пользователю.");
    }
}
