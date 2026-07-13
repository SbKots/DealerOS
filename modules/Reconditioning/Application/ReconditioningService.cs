using System.Text.Json;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Inspections.Domain;
using DealerOS.Modules.Reconditioning.Domain;
using DealerOS.Modules.Vehicles.Application;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Reconditioning.Application;

public sealed class ReconditioningService(IReconditioningStore store, IAuditWriter audit, TimeProvider timeProvider)
{
    public Task<IReadOnlyList<ReconditioningQueueVehicleResponse>> ListRequiredVehiclesAsync(ActorContext actor,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReconditioningView);
        return store.ListRequiredVehiclesAsync(actor.OrganizationId, actor.BranchIds, cancellationToken);
    }

    public Task<IReadOnlyList<ReconditioningPlanSummaryResponse>> ListApprovalsAsync(ActorContext actor,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReconditioningApprove);
        return store.ListApprovalsAsync(actor.OrganizationId, actor.BranchIds, cancellationToken);
    }

    public Task<IReadOnlyList<ReconditioningPlanSummaryResponse>> ListForVehicleAsync(ActorContext actor,
        Guid vehicleId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReconditioningView);
        return store.ListForVehicleAsync(actor.OrganizationId, vehicleId, actor.BranchIds, cancellationToken);
    }

    public async Task<ReconditioningPlanDetailResponse> GetAsync(ActorContext actor, Guid planId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReconditioningView);
        return await GetRequiredResponseAsync(actor, planId, cancellationToken);
    }

    public async Task<ReconditioningPlanDetailResponse> CreateAsync(ActorContext actor, Guid vehicleId,
        CreateReconditioningPlanRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReconditioningCreate);
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        DemandBranch(actor, vehicle.BranchId);
        if (vehicle.Status != VehicleStatus.ReconditioningRequired)
            throw new DomainException("reconditioning.vehicle_not_required",
                "План подготовки доступен только автомобилю после осмотра с требованием подготовки.");

        var active = await store.FindActiveAsync(actor.OrganizationId, vehicleId, cancellationToken);
        if (active is not null) return await GetRequiredResponseAsync(actor, active.Id, cancellationToken);
        var latest = await store.FindLatestAsync(actor.OrganizationId, vehicleId, cancellationToken);
        if (latest?.Status == ReconditioningPlanStatus.Approved)
            throw new DomainException("reconditioning.approved_requires_revision",
                "Для изменения утверждённого плана создайте новую ревизию отдельной командой.");

        var inspection = await store.FindInspectionAsync(actor.OrganizationId, request.InspectionId, cancellationToken)
            ?? throw new NotFoundException("Исходный осмотр не найден.");
        if (inspection.VehicleId != vehicleId || inspection.BranchId != vehicle.BranchId
            || inspection.Status != InspectionStatus.Completed || !inspection.NeedsReconditioning)
            throw new DomainException("reconditioning.invalid_source_inspection",
                "План можно создать только из завершённого осмотра этого автомобиля с обязательными дефектами.");

        var now = timeProvider.GetUtcNow();
        var plan = ReconditioningPlan.Create(actor.OrganizationId, vehicle.BranchId, vehicleId, inspection.Id,
            actor.UserId, inspection.Defects.Select(MapDefect), vehicle.Currency, now,
            latest is null ? 1 : latest.Revision + 1, latest?.Id);
        await store.AddAsync(plan, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "reconditioning.plan_created", "ReconditioningPlan",
            plan.Id, null, JsonSerializer.Serialize(new
            {
                plan.VehicleId,
                plan.SourceInspectionId,
                plan.Revision,
                mandatoryDefectIds = plan.Works.Where(x => x.IsMandatory).Select(x => x.SourceDefectId),
                works = plan.Works.Select(WorkAudit)
            }), correlationId, now);
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException exception) when (exception.Code == "reconditioning.active_exists")
        {
            store.ResetTracking();
            active = await store.FindActiveAsync(actor.OrganizationId, vehicleId, CancellationToken.None);
            if (active is not null) return await GetRequiredResponseAsync(actor, active.Id, CancellationToken.None);
            throw;
        }
        return await GetRequiredResponseAsync(actor, plan.Id, cancellationToken);
    }

    public async Task<ReconditioningPlanDetailResponse> AddWorkAsync(ActorContext actor, Guid planId,
        AddReconditioningWorkRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReconditioningEdit);
        var plan = await FindScopedAsync(actor, planId, cancellationToken);
        var inspection = await store.FindInspectionAsync(actor.OrganizationId, plan.SourceInspectionId, cancellationToken)
            ?? throw new NotFoundException("Исходный осмотр не найден.");
        var defect = inspection.Defects.SingleOrDefault(x => x.Id == request.SourceDefectId)
            ?? throw new DomainException("reconditioning.defect_not_in_source",
                "Работа должна ссылаться на дефект исходного осмотра.");
        var now = timeProvider.GetUtcNow();
        var work = plan.AddWork(request.WorkId, MapDefect(defect), request.Title, request.Description,
            request.Category, request.Priority, request.IsMandatory, request.ExecutorType, request.ExecutorName,
            request.EstimatedLaborAmount, request.EstimatedPartsAmount, request.Currency,
            request.EstimatedDurationDays, request.Comment, request.ExpectedVersion, now);
        audit.Write(actor.OrganizationId, actor.UserId, "reconditioning.work_added", "ReconditioningWork", work.Id,
            null, JsonSerializer.Serialize(WorkAudit(work)), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetRequiredResponseAsync(actor, plan.Id, cancellationToken);
    }

    public async Task<ReconditioningPlanDetailResponse> UpdateWorkAsync(ActorContext actor, Guid planId, Guid workId,
        UpdateReconditioningWorkRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReconditioningEdit);
        var plan = await FindScopedAsync(actor, planId, cancellationToken);
        var existing = plan.Works.SingleOrDefault(x => x.Id == workId)
            ?? throw new NotFoundException("Работа плана не найдена.");
        var oldValue = JsonSerializer.Serialize(WorkAudit(existing));
        var now = timeProvider.GetUtcNow();
        var work = plan.UpdateWork(workId, request.Title, request.Description, request.Category, request.Priority,
            request.IsMandatory, request.ExecutorType, request.ExecutorName, request.EstimatedLaborAmount,
            request.EstimatedPartsAmount, request.Currency, request.EstimatedDurationDays, request.Comment,
            request.ExpectedVersion, now);
        audit.Write(actor.OrganizationId, actor.UserId, "reconditioning.work_updated", "ReconditioningWork", work.Id,
            oldValue, JsonSerializer.Serialize(WorkAudit(work)), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetRequiredResponseAsync(actor, plan.Id, cancellationToken);
    }

    public async Task<ReconditioningPlanDetailResponse> RemoveWorkAsync(ActorContext actor, Guid planId, Guid workId,
        RemoveReconditioningWorkRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReconditioningEdit);
        var plan = await FindScopedAsync(actor, planId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var removed = plan.RemoveWork(workId, request.OmissionReason, actor.UserId, request.ExpectedVersion, now);
        audit.Write(actor.OrganizationId, actor.UserId, "reconditioning.work_removed", "ReconditioningWork",
            removed.Id, JsonSerializer.Serialize(WorkAudit(removed)), JsonSerializer.Serialize(new
            {
                removed = true,
                request.OmissionReason
            }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetRequiredResponseAsync(actor, plan.Id, cancellationToken);
    }

    public async Task<ReconditioningPlanDetailResponse> SubmitAsync(ActorContext actor, Guid planId,
        SubmitReconditioningPlanRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReconditioningSubmit);
        var plan = await FindScopedAsync(actor, planId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var oldStatus = plan.Status;
        plan.Submit(actor.UserId, request.ExpectedVersion, now);
        audit.Write(actor.OrganizationId, actor.UserId, "reconditioning.plan_submitted", "ReconditioningPlan",
            plan.Id, JsonSerializer.Serialize(new { status = oldStatus.ToString() }), JsonSerializer.Serialize(new
            {
                status = plan.Status.ToString(),
                works = plan.Works.Select(WorkAudit),
                omissions = plan.Omissions.Select(x => new { x.SourceDefectId, x.Reason })
            }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetRequiredResponseAsync(actor, plan.Id, cancellationToken);
    }

    public Task<ReconditioningPlanDetailResponse> ApproveAsync(ActorContext actor, Guid planId,
        ApproveReconditioningPlanRequest request, string correlationId, CancellationToken cancellationToken) =>
        ExecuteDecisionAsync(actor, planId, request.DecisionId, ReconditioningDecisionType.Approved,
            request.ExpectedVersion, request.Comment, request.ApprovedLimitAmount, request.Currency, correlationId,
            cancellationToken);

    public Task<ReconditioningPlanDetailResponse> RejectAsync(ActorContext actor, Guid planId,
        RejectReconditioningPlanRequest request, string correlationId, CancellationToken cancellationToken) =>
        ExecuteDecisionAsync(actor, planId, request.DecisionId, ReconditioningDecisionType.Rejected,
            request.ExpectedVersion, request.Reason, null, null, correlationId, cancellationToken);

    public Task<ReconditioningPlanDetailResponse> RequestChangesAsync(ActorContext actor, Guid planId,
        RequestReconditioningChangesRequest request, string correlationId, CancellationToken cancellationToken) =>
        ExecuteDecisionAsync(actor, planId, request.DecisionId, ReconditioningDecisionType.ChangesRequested,
            request.ExpectedVersion, request.Reason, null, null, correlationId, cancellationToken);

    public async Task<ReconditioningPlanDetailResponse> CancelAsync(ActorContext actor, Guid planId,
        CancelReconditioningPlanRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReconditioningCancel);
        var plan = await FindScopedAsync(actor, planId, cancellationToken);
        if (plan.Status == ReconditioningPlanStatus.Cancelled)
            return await GetRequiredResponseAsync(actor, plan.Id, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var oldStatus = plan.Status;
        plan.Cancel(actor.UserId, request.Reason, request.ExpectedVersion, now);
        audit.Write(actor.OrganizationId, actor.UserId, "reconditioning.plan_cancelled", "ReconditioningPlan",
            plan.Id, JsonSerializer.Serialize(new { status = oldStatus.ToString() }), JsonSerializer.Serialize(new
            {
                status = plan.Status.ToString(),
                request.Reason
            }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetRequiredResponseAsync(actor, plan.Id, cancellationToken);
    }

    public async Task<ReconditioningPlanDetailResponse> CreateRevisionAsync(ActorContext actor, Guid planId,
        CreateReconditioningRevisionRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReconditioningCreate);
        Demand(actor, Permissions.ReconditioningEdit);
        var source = await FindScopedAsync(actor, planId, cancellationToken);
        if (source.Version != request.ExpectedVersion)
            throw new ConflictException("reconditioning.version_conflict",
                "План был изменён другим пользователем. Обновите данные.");
        var active = await store.FindActiveAsync(actor.OrganizationId, source.VehicleId, cancellationToken);
        if (active is not null) return await GetRequiredResponseAsync(actor, active.Id, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var revision = ReconditioningPlan.CreateRevision(source, actor.UserId, now);
        await store.AddAsync(revision, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "reconditioning.revision_created", "ReconditioningPlan",
            revision.Id, null, JsonSerializer.Serialize(new
            {
                sourcePlanId = source.Id,
                revision.Revision,
                works = revision.Works.Select(WorkAudit)
            }), correlationId, now);
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException exception) when (exception.Code == "reconditioning.active_exists")
        {
            store.ResetTracking();
            active = await store.FindActiveAsync(actor.OrganizationId, source.VehicleId, CancellationToken.None);
            if (active is not null) return await GetRequiredResponseAsync(actor, active.Id, CancellationToken.None);
            throw;
        }
        return await GetRequiredResponseAsync(actor, revision.Id, cancellationToken);
    }

    private async Task<ReconditioningPlanDetailResponse> ExecuteDecisionAsync(ActorContext actor, Guid planId,
        Guid decisionId, ReconditioningDecisionType type, long expectedVersion, string? reason,
        decimal? approvedLimitAmount, string? currency, string correlationId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReconditioningApprove);
        if (decisionId == Guid.Empty)
            throw new DomainException("reconditioning.decision_id_required", "Идентификатор решения обязателен.");
        var plan = await FindScopedAsync(actor, planId, cancellationToken);
        if (await store.RequiresIndependentApprovalAsync(actor.OrganizationId, cancellationToken)
            && plan.CreatedByUserId == actor.UserId)
            throw new ForbiddenException("Автор плана не может согласовать собственный план.");
        var now = timeProvider.GetUtcNow();
        var changed = type switch
        {
            ReconditioningDecisionType.Approved => plan.Approve(decisionId, actor.UserId, approvedLimitAmount,
                currency, reason, expectedVersion, now),
            ReconditioningDecisionType.Rejected => plan.Reject(decisionId, actor.UserId, reason,
                expectedVersion, now),
            ReconditioningDecisionType.ChangesRequested => plan.RequestChanges(decisionId, actor.UserId, reason,
                expectedVersion, now),
            _ => throw new DomainException("reconditioning.invalid_decision", "Недопустимое решение.")
        };
        if (!changed) return await GetRequiredResponseAsync(actor, plan.Id, cancellationToken);

        audit.Write(actor.OrganizationId, actor.UserId, $"reconditioning.{type.ToString().ToLowerInvariant()}",
            "ReconditioningDecision", decisionId, null, JsonSerializer.Serialize(new
            {
                planId,
                type = type.ToString(),
                reason,
                approvedLimitAmount,
                currency
            }), correlationId, now);
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            store.ResetTracking();
            var current = await store.FindAsync(actor.OrganizationId, planId, CancellationToken.None);
            if (current?.MatchesDecisionCommand(decisionId, type, actor.UserId, reason, approvedLimitAmount,
                currency) == true)
                return await GetRequiredResponseAsync(actor, planId, CancellationToken.None);
            throw;
        }
        return await GetRequiredResponseAsync(actor, plan.Id, cancellationToken);
    }

    private async Task<ReconditioningPlan> FindScopedAsync(ActorContext actor, Guid planId,
        CancellationToken cancellationToken)
    {
        var plan = await store.FindAsync(actor.OrganizationId, planId, cancellationToken)
            ?? throw new NotFoundException("План подготовки не найден.");
        DemandBranch(actor, plan.BranchId);
        return plan;
    }

    private async Task<ReconditioningPlanDetailResponse> GetRequiredResponseAsync(ActorContext actor, Guid planId,
        CancellationToken cancellationToken) => await store.GetResponseAsync(actor.OrganizationId, planId,
        actor.BranchIds, cancellationToken) ?? throw new NotFoundException("План подготовки не найден.");

    private static SourceDefectForPlan MapDefect(InspectionDefect defect) => new(defect.Id, defect.Title,
        defect.Description, defect.Recommendation, defect.Category, defect.Severity, defect.EstimatedRepairAmount,
        defect.Currency, defect.RepairRequired);

    private static object WorkAudit(ReconditioningWork work) => new
    {
        work.SourceDefectId,
        work.Title,
        work.Category,
        work.Priority,
        work.IsMandatory,
        work.ExecutorType,
        work.ExecutorName,
        work.EstimatedLaborAmount,
        work.EstimatedPartsAmount,
        work.Currency,
        work.EstimatedDurationDays,
        work.Comment
    };

    private static void Demand(ActorContext actor, string permission)
    {
        if (!actor.Permissions.Contains(permission))
            throw new ForbiddenException("Недостаточно прав для операции с планом подготовки.");
    }

    private static void DemandBranch(ActorContext actor, Guid branchId)
    {
        if (!actor.BranchIds.Contains(branchId)) throw new ForbiddenException("Филиал недоступен пользователю.");
    }
}
