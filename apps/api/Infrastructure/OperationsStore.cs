using DealerOS.Modules.Operations.Application;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Reconditioning.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DealerOS.Api.Infrastructure;

public sealed class OperationsStore(DealerOsDbContext dbContext) : IOperationsStore
{
    public Task<ReconditioningPlan?> FindPlanAsync(Guid organizationId, Guid planId,
        CancellationToken cancellationToken) => dbContext.ReconditioningPlans
        .Include(x => x.Works).Include(x => x.BudgetSnapshots)
        .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == planId, cancellationToken);

    public Task<ReconditioningExecution?> FindExecutionAsync(Guid organizationId, Guid executionId,
        CancellationToken cancellationToken) => AggregateQuery().SingleOrDefaultAsync(
        x => x.OrganizationId == organizationId && x.Id == executionId, cancellationToken);

    public Task<ReconditioningExecution?> FindBySnapshotAsync(Guid organizationId, Guid budgetSnapshotId,
        CancellationToken cancellationToken) => AggregateQuery().SingleOrDefaultAsync(
        x => x.OrganizationId == organizationId && x.BudgetSnapshotId == budgetSnapshotId, cancellationToken);

    public async Task<IReadOnlyList<ReconditioningExecution>> ListForVehicleAsync(Guid organizationId,
        Guid vehicleId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) => await AggregateQuery()
        .Where(x => x.OrganizationId == organizationId && x.VehicleId == vehicleId
                    && branchIds.Contains(x.BranchId))
        .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReconditioningExecution>> ListActiveAsync(
        CancellationToken cancellationToken) => await AggregateQuery()
        .Where(x => x.Status == ReconditioningExecutionStatus.InProgress
                    || x.Status == ReconditioningExecutionStatus.Blocked)
        .ToListAsync(cancellationToken);

    public Task<bool> RequiresIndependentApprovalAsync(Guid organizationId,
        CancellationToken cancellationToken) => dbContext.Organizations.Where(x => x.Id == organizationId)
        .Select(x => x.RequireIndependentReconditioningApproval).SingleAsync(cancellationToken);

    public Task AddAsync(ReconditioningExecution execution, CancellationToken cancellationToken) =>
        dbContext.ReconditioningExecutions.AddAsync(execution, cancellationToken).AsTask();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("operations.version_conflict", "Execution был изменён конкурентно.");
        }
        catch (DbUpdateException exception) when (FindPostgres(exception, PostgresErrorCodes.UniqueViolation)
                                                   is { ConstraintName: "ux_operations_execution_snapshot" })
        {
            throw new ConflictException("operations.execution_exists",
                "Execution для утверждённого snapshot уже существует.");
        }
    }

    public void ResetTracking() => dbContext.ChangeTracker.Clear();

    private IQueryable<ReconditioningExecution> AggregateQuery() => dbContext.ReconditioningExecutions
        .Include(x => x.WorkOrders).ThenInclude(x => x.MaterialMovements)
        .Include(x => x.OverrunDecisions).Include(x => x.Notifications).AsSplitQuery();

    private static PostgresException? FindPostgres(Exception exception, params string[] sqlStates)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && sqlStates.Contains(postgres.SqlState)) return postgres;
        return null;
    }
}
