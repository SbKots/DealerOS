using DealerOS.Modules.Inspections.Domain;
using DealerOS.Modules.Reconditioning.Application;
using DealerOS.Modules.Reconditioning.Domain;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DealerOS.Api.Infrastructure;

public sealed class ReconditioningStore(DealerOsDbContext dbContext) : IReconditioningStore
{
    public Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken) =>
        dbContext.Vehicles.Include(x => x.StatusHistory)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == vehicleId, cancellationToken);

    public Task<Inspection?> FindInspectionAsync(Guid organizationId, Guid inspectionId,
        CancellationToken cancellationToken) => dbContext.Inspections.Include(x => x.Defects)
        .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == inspectionId, cancellationToken);

    public Task<ReconditioningPlan?> FindAsync(Guid organizationId, Guid planId,
        CancellationToken cancellationToken) => AggregateQuery().SingleOrDefaultAsync(
        x => x.OrganizationId == organizationId && x.Id == planId, cancellationToken);

    public Task<ReconditioningPlan?> FindActiveAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => AggregateQuery().SingleOrDefaultAsync(x =>
        x.OrganizationId == organizationId && x.VehicleId == vehicleId
        && (x.Status == ReconditioningPlanStatus.Draft || x.Status == ReconditioningPlanStatus.Submitted
            || x.Status == ReconditioningPlanStatus.ChangesRequested), cancellationToken);

    public Task<ReconditioningPlan?> FindLatestAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => AggregateQuery().Where(x =>
        x.OrganizationId == organizationId && x.VehicleId == vehicleId).OrderByDescending(x => x.Revision)
        .ThenByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> RequiresIndependentApprovalAsync(Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.Organizations.Where(x => x.Id == organizationId)
            .Select(x => x.RequireIndependentReconditioningApproval).SingleAsync(cancellationToken);

    public async Task AddAsync(ReconditioningPlan plan, CancellationToken cancellationToken) =>
        await dbContext.ReconditioningPlans.AddAsync(plan, cancellationToken);

    public async Task<IReadOnlyList<ReconditioningQueueVehicleResponse>> ListRequiredVehiclesAsync(
        Guid organizationId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken)
    {
        var vehicles = await dbContext.Vehicles.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && branchIds.Contains(x.BranchId)
                && x.Status == VehicleStatus.ReconditioningRequired)
            .OrderBy(x => x.AcceptedAt).ToListAsync(cancellationToken);
        if (vehicles.Count == 0) return [];
        var vehicleIds = vehicles.Select(x => x.Id).ToArray();
        var plans = await dbContext.ReconditioningPlans.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && vehicleIds.Contains(x.VehicleId))
            .ToListAsync(cancellationToken);
        var latestPlans = plans.GroupBy(x => x.VehicleId).ToDictionary(x => x.Key,
            x => x.OrderByDescending(plan => plan.Revision).ThenByDescending(plan => plan.UpdatedAt).First());
        var activeVehicleIds = plans.Where(x => x.Status is ReconditioningPlanStatus.Draft
            or ReconditioningPlanStatus.Submitted or ReconditioningPlanStatus.ChangesRequested)
            .Select(x => x.VehicleId).ToHashSet();
        var inspections = await dbContext.Inspections.AsNoTracking().Include(x => x.Defects)
            .Where(x => x.OrganizationId == organizationId && vehicleIds.Contains(x.VehicleId)
                && x.Status == InspectionStatus.Completed && x.Defects.Any(d => d.RepairRequired))
            .ToListAsync(cancellationToken);
        var latestByVehicle = inspections.GroupBy(x => x.VehicleId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(i => i.Revision)
                .ThenByDescending(i => i.CompletedAt).First());
        var branchNames = await dbContext.Branches.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && branchIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        return vehicles.Where(x => latestByVehicle.ContainsKey(x.Id)).Select(vehicle =>
        {
            var inspection = latestByVehicle[vehicle.Id];
            latestPlans.TryGetValue(vehicle.Id, out var latestPlan);
            return new ReconditioningQueueVehicleResponse(vehicle.Id, vehicle.BranchId,
                branchNames[vehicle.BranchId], vehicle.Vin, vehicle.Make, vehicle.Model, vehicle.Year,
                vehicle.StockNumber, inspection.Id, inspection.CompletedAt!.Value,
                inspection.Defects.Count(x => x.RepairRequired), activeVehicleIds.Contains(vehicle.Id),
                latestPlan?.Id, latestPlan?.Status.ToString(), latestPlan?.Revision);
        }).ToArray();
    }

    public async Task<IReadOnlyList<ReconditioningPlanSummaryResponse>> ListApprovalsAsync(Guid organizationId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) => await ListSummariesAsync(
        organizationId, branchIds, x => x.Status == ReconditioningPlanStatus.Submitted, cancellationToken);

    public async Task<IReadOnlyList<ReconditioningPlanSummaryResponse>> ListForVehicleAsync(Guid organizationId,
        Guid vehicleId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) => await ListSummariesAsync(
        organizationId, branchIds, x => x.VehicleId == vehicleId, cancellationToken);

    public async Task<ReconditioningPlanDetailResponse?> GetResponseAsync(Guid organizationId, Guid planId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken)
    {
        var plan = await AggregateQuery().AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.Id == planId && branchIds.Contains(x.BranchId),
            cancellationToken);
        if (plan is null) return null;
        var vehicle = await dbContext.Vehicles.AsNoTracking().SingleAsync(x =>
            x.OrganizationId == organizationId && x.Id == plan.VehicleId, cancellationToken);
        var branchName = await dbContext.Branches.AsNoTracking().Where(x =>
            x.OrganizationId == organizationId && x.Id == plan.BranchId).Select(x => x.Name)
            .SingleAsync(cancellationToken);
        var userIds = plan.History.Select(x => x.ActorUserId)
            .Concat(plan.Decisions.Select(x => x.ActorUserId))
            .Concat(plan.Omissions.Select(x => x.DecidedByUserId))
            .Concat(plan.BudgetSnapshots.Select(x => x.ApprovedByUserId))
            .Append(plan.CreatedByUserId).Distinct().ToArray();
        var users = await dbContext.Users.AsNoTracking().Where(x =>
            x.OrganizationId == organizationId && userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        return MapDetail(plan, vehicle, branchName, users);
    }

    public void ResetTracking() => dbContext.ChangeTracker.Clear();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("reconditioning.version_conflict",
                "План был изменён другим пользователем. Обновите данные.");
        }
        catch (DbUpdateException exception) when (FindPostgres(exception, PostgresErrorCodes.UniqueViolation) is { } postgres)
        {
            throw new ConflictException(postgres.ConstraintName switch
            {
                "ux_reconditioning_plans_active_vehicle" => "reconditioning.active_exists",
                "ux_reconditioning_decisions_command" => "reconditioning.decision_id_conflict",
                _ => "database.unique_constraint"
            }, postgres.ConstraintName switch
            {
                "ux_reconditioning_plans_active_vehicle" => "У автомобиля уже есть активный план подготовки.",
                "ux_reconditioning_decisions_command" => "Идентификатор решения уже использован.",
                _ => "Запись конфликтует с существующими данными."
            });
        }
        catch (Exception exception) when (FindPostgres(exception, PostgresErrorCodes.DeadlockDetected,
            PostgresErrorCodes.SerializationFailure) is not null)
        {
            throw new ConflictException("reconditioning.concurrency_conflict",
                "Операция конфликтует с параллельным изменением.");
        }
    }

    private IQueryable<ReconditioningPlan> AggregateQuery() => dbContext.ReconditioningPlans
        .Include(x => x.Works).Include(x => x.Omissions).Include(x => x.Decisions)
        .Include(x => x.BudgetSnapshots).Include(x => x.History);

    private async Task<IReadOnlyList<ReconditioningPlanSummaryResponse>> ListSummariesAsync(Guid organizationId,
        IReadOnlySet<Guid> branchIds, System.Linq.Expressions.Expression<Func<ReconditioningPlan, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var plans = await dbContext.ReconditioningPlans.AsNoTracking().Include(x => x.Works)
            .Where(x => x.OrganizationId == organizationId && branchIds.Contains(x.BranchId)).Where(predicate)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync(cancellationToken);
        if (plans.Count == 0) return [];
        var vehicleIds = plans.Select(x => x.VehicleId).Distinct().ToArray();
        var branchIdsUsed = plans.Select(x => x.BranchId).Distinct().ToArray();
        var userIds = plans.Select(x => x.CreatedByUserId).Distinct().ToArray();
        var vehicles = await dbContext.Vehicles.AsNoTracking().Where(x =>
            x.OrganizationId == organizationId && vehicleIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id,
            cancellationToken);
        var branches = await dbContext.Branches.AsNoTracking().Where(x =>
            x.OrganizationId == organizationId && branchIdsUsed.Contains(x.Id)).ToDictionaryAsync(x => x.Id,
            x => x.Name, cancellationToken);
        var users = await dbContext.Users.AsNoTracking().Where(x =>
            x.OrganizationId == organizationId && userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id,
            x => x.DisplayName, cancellationToken);
        return plans.Select(plan => MapSummary(plan, vehicles[plan.VehicleId], branches[plan.BranchId],
            users[plan.CreatedByUserId])).ToArray();
    }

    private static ReconditioningPlanSummaryResponse MapSummary(ReconditioningPlan plan, Vehicle vehicle,
        string branchName, string createdByName) => new(plan.Id, plan.VehicleId, plan.BranchId, branchName,
        vehicle.Vin, vehicle.Make, vehicle.Model, plan.Status.ToString(), plan.Revision, plan.SourceInspectionId,
        plan.RevisesPlanId, plan.CreatedByUserId, createdByName, plan.Works.Count, MapBudgets(plan.Works),
        plan.Version, plan.CreatedAt, plan.UpdatedAt);

    private static ReconditioningPlanDetailResponse MapDetail(ReconditioningPlan plan, Vehicle vehicle,
        string branchName, IReadOnlyDictionary<Guid, string> users) => new(plan.Id, plan.VehicleId, plan.BranchId,
        branchName, vehicle.Vin, vehicle.Make, vehicle.Model, vehicle.StockNumber, plan.SourceInspectionId,
        plan.CreatedByUserId, users[plan.CreatedByUserId], plan.Status.ToString(), plan.Revision,
        plan.RevisesPlanId, plan.SubmittedAt, plan.DecidedAt, plan.CancelledAt, plan.Version, plan.CreatedAt,
        plan.UpdatedAt, MapBudgets(plan.Works), plan.Works.OrderByDescending(x => x.IsMandatory)
            .ThenByDescending(x => x.Priority).ThenBy(x => x.CreatedAt).Select(x => new ReconditioningWorkResponse(
                x.Id, x.SourceDefectId, x.SourceDefectTitle, x.SourceDefectDescription, x.SourceDefectSeverity,
                x.Title, x.Description, x.Category.ToString(), x.Priority.ToString(), x.IsMandatory,
                x.ExecutorType.ToString(), x.ExecutorName, x.EstimatedLaborAmount, x.EstimatedPartsAmount,
                x.Currency, x.EstimatedDurationDays, x.Comment, x.CreatedAt, x.UpdatedAt)).ToArray(),
        plan.Omissions.OrderBy(x => x.DecidedAt).Select(x => new ReconditioningOmissionResponse(x.Id,
            x.SourceDefectId, x.SourceDefectTitle, x.Reason, x.DecidedByUserId, users[x.DecidedByUserId],
            x.DecidedAt)).ToArray(), plan.Decisions.OrderBy(x => x.DecidedAt).Select(x =>
            new ReconditioningDecisionResponse(x.Id, x.Type.ToString(), x.ActorUserId, users[x.ActorUserId],
                x.Reason, x.ApprovedLimitAmount, x.Currency, x.DecidedAt)).ToArray(),
        plan.BudgetSnapshots.OrderBy(x => x.ApprovedAt).Select(x => new ReconditioningBudgetSnapshotResponse(x.Id,
            x.LaborAmount, x.PartsAmount, x.PlannedTotalAmount, x.ApprovedLimitAmount, x.Currency,
            x.ApprovedByUserId, users[x.ApprovedByUserId], x.ApprovedAt)).ToArray(),
        plan.History.OrderBy(x => x.OccurredAt).Select(x => new ReconditioningHistoryResponse(x.Id,
            x.FromStatus?.ToString(), x.ToStatus.ToString(), x.ActorUserId, users[x.ActorUserId], x.Reason,
            x.DecisionId, x.OccurredAt)).ToArray());

    private static IReadOnlyList<ReconditioningBudgetResponse> MapBudgets(
        IEnumerable<ReconditioningWork> works) => works.GroupBy(x => x.Currency).OrderBy(x => x.Key)
        .Select(group => new ReconditioningBudgetResponse(group.Key, group.Sum(x => x.EstimatedLaborAmount),
            group.Sum(x => x.EstimatedPartsAmount), group.Sum(x => x.EstimatedLaborAmount + x.EstimatedPartsAmount)))
        .ToArray();

    private static PostgresException? FindPostgres(Exception exception, params string[] sqlStates)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && sqlStates.Contains(postgres.SqlState)) return postgres;
        return null;
    }
}
