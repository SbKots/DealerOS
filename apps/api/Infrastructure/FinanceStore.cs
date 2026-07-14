using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DealerOS.Modules.Deals.Application;
using DealerOS.Modules.Deals.Domain;
using DealerOS.Modules.Finance.Application;
using DealerOS.Modules.Finance.Domain;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DealerOS.Api.Infrastructure;

public sealed class FinanceStore(DealerOsDbContext dbContext) : IFinanceStore, IProfitSnapshotWriter
{
    public Task<Deal?> FindDealAsync(Guid organizationId, Guid dealId, CancellationToken cancellationToken) =>
        DealQuery().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == dealId,
            cancellationToken);
    public Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => dbContext.Vehicles.AsNoTracking().SingleOrDefaultAsync(x =>
        x.OrganizationId == organizationId && x.Id == vehicleId, cancellationToken);
    public Task<ManualCostEntry?> FindCostAsync(Guid organizationId, Guid costId,
        CancellationToken cancellationToken) => dbContext.ManualCostEntries.SingleOrDefaultAsync(x =>
        x.OrganizationId == organizationId && x.Id == costId, cancellationToken);
    public Task<ManualCostEntry?> FindCostByCommandAsync(Guid organizationId, Guid commandId,
        CancellationToken cancellationToken) => dbContext.ManualCostEntries.SingleOrDefaultAsync(x =>
        x.OrganizationId == organizationId && x.CommandId == commandId, cancellationToken);
    public Task<bool> IsSupersededAsync(Guid organizationId, Guid costId, CancellationToken cancellationToken) =>
        dbContext.ManualCostEntries.AsNoTracking().AnyAsync(x => x.OrganizationId == organizationId
            && x.SupersedesEntryId == costId, cancellationToken);
    public Task AddCostAsync(ManualCostEntry entry, CancellationToken cancellationToken) =>
        dbContext.ManualCostEntries.AddAsync(entry, cancellationToken).AsTask();

    public async Task CaptureAsync(Deal deal, Guid actorUserId, string reason, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var vehicle = await dbContext.Vehicles.AsNoTracking().SingleAsync(x => x.OrganizationId == deal.OrganizationId
            && x.Id == deal.VehicleId, cancellationToken);
        var executions = await dbContext.ReconditioningExecutions.AsNoTracking().Include(x => x.WorkOrders)
            .ThenInclude(x => x.MaterialMovements).Where(x => x.OrganizationId == deal.OrganizationId
                && x.VehicleId == deal.VehicleId && x.Status == ReconditioningExecutionStatus.Completed)
            .ToListAsync(cancellationToken);
        var storedCosts = await dbContext.ManualCostEntries.AsNoTracking().Where(x =>
            x.OrganizationId == deal.OrganizationId && x.DealId == deal.Id).ToListAsync(cancellationToken);
        var pendingCosts = dbContext.ChangeTracker.Entries<ManualCostEntry>().Where(x =>
            x.State == EntityState.Added && x.Entity.OrganizationId == deal.OrganizationId
            && x.Entity.DealId == deal.Id).Select(x => x.Entity);
        var allCosts = storedCosts.Concat(pendingCosts).GroupBy(x => x.Id).Select(x => x.Last()).ToArray();
        var superseded = allCosts.Where(x => x.SupersedesEntryId is not null)
            .Select(x => x.SupersedesEntryId!.Value).ToHashSet();
        var activeCosts = allCosts.Where(x => !superseded.Contains(x.Id)).OrderBy(x => x.Id).ToArray();
        var currencies = new[] { deal.Currency, vehicle.Currency }
            .Concat(executions.Select(x => x.Currency)).Concat(activeCosts.Select(x => x.Currency))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (currencies.Length != 1)
        {
            var sources = new[] { $"Deal:{deal.Id}={deal.Currency}", $"Vehicle:{vehicle.Id}={vehicle.Currency}" }
                .Concat(executions.Select(x => $"Execution:{x.Id}={x.Currency}"))
                .Concat(activeCosts.Select(x => $"ManualCost:{x.Id}={x.Currency}"));
            throw new DomainException("finance.mixed_currency",
                $"Расчёт заблокирован: {string.Join(", ", sources)}.");
        }

        var result = ProfitCalculator.Calculate(deal.Currency, deal.FinalTotalAmount, deal.RefundedTotal,
            vehicle.PlannedPurchaseAmount,
            executions.Select(x => new CostSourceAmount(x.ActualTotalAmount, x.Currency)),
            activeCosts.Select(x => new CostSourceAmount(x.Amount, x.Currency)), deal.ExpectedMarginAmount);
        var sourcesJson = JsonSerializer.Serialize(new
        {
            formula = "DealerOS.Profit.v1",
            purchase = new { vehicle.Id, amount = result.PurchaseCost, vehicle.Currency },
            revenue = new
            {
                deal.Id,
                approvedOfferSnapshotId = deal.ApprovedOfferSnapshotId,
                amount = result.GrossRevenue,
                deal.Currency
            },
            refunds = deal.Payments.Where(x => x.Kind == PaymentKind.Refund && x.Status == PaymentStatus.Refunded)
                .OrderBy(x => x.Id).Select(x => new { x.Id, x.Amount, x.Currency }).ToArray(),
            paymentReconciliation = deal.Payments.OrderBy(x => x.Id).Select(x => new
            { x.Id, kind = x.Kind.ToString(), status = x.Status.ToString(), x.Amount, x.Currency }).ToArray(),
            operations = executions.OrderBy(x => x.Id).Select(x => new
            {
                x.Id,
                amount = x.ActualTotalAmount,
                x.Currency,
                labor = x.ActualLaborAmount,
                materials = x.ActualMaterialAmount,
                external = x.ActualExternalAmount,
                workOrders = x.WorkOrders.OrderBy(work => work.Id).Select(work => new
                {
                    work.Id,
                    labor = work.ActualLaborAmount,
                    materials = work.MaterialAmount,
                    external = work.ActualExternalAmount,
                    work.Currency,
                    materialMovements = work.MaterialMovements.OrderBy(movement => movement.Id).Select(movement =>
                        new { movement.Id, amount = movement.SignedAmount, movement.Currency }).ToArray()
                }).ToArray()
            }).ToArray(),
            manualCosts = activeCosts.Select(x => new
            {
                x.Id,
                category = x.Category.ToString(),
                x.Amount,
                x.Currency,
                x.Reference,
                x.SupersedesEntryId
            }).ToArray()
        });
        var calculation = JsonSerializer.Serialize(new
        {
            sourcesJson,
            result.GrossRevenue,
            result.Refunds,
            result.NetRevenue,
            result.PurchaseCost,
            result.OperationsCost,
            result.ManualCost,
            result.TotalCost,
            result.ActualProfit,
            result.ActualMarginPercent,
            result.PlanProfit,
            result.PlanMarginPercent
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(calculation))).ToLowerInvariant();
        var previous = await dbContext.ProfitSnapshots.Where(x => x.OrganizationId == deal.OrganizationId
            && x.DealId == deal.Id).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(cancellationToken);
        if (previous?.Sha256 == hash) return;
        var snapshot = ProfitSnapshot.Create(Guid.NewGuid(), deal.OrganizationId, deal.BranchId, deal.Id,
            deal.VehicleId, (previous?.Revision ?? 0) + 1, previous?.Id, reason, "DealerOS.Profit.v1",
            deal.Currency, result.GrossRevenue, result.Refunds, result.NetRevenue, result.PurchaseCost,
            result.OperationsCost, result.ManualCost, result.TotalCost, result.ActualProfit,
            result.ActualMarginPercent, result.PlanProfit, result.PlanMarginPercent, sourcesJson, hash,
            actorUserId, now);
        await dbContext.ProfitSnapshots.AddAsync(snapshot, cancellationToken);
    }

    public async Task<IReadOnlyList<ProfitSnapshot>> ListSnapshotsAsync(Guid organizationId, Guid dealId,
        CancellationToken cancellationToken) => await dbContext.ProfitSnapshots.AsNoTracking().Where(x =>
        x.OrganizationId == organizationId && x.DealId == dealId).OrderBy(x => x.Revision)
        .ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<ManualCostEntry>> ListCostsAsync(Guid organizationId, Guid dealId,
        CancellationToken cancellationToken) => await dbContext.ManualCostEntries.AsNoTracking().Where(x =>
        x.OrganizationId == organizationId && x.DealId == dealId).OrderBy(x => x.RecordedAt)
        .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FinanceDashboardRow>> ListDashboardAsync(Guid organizationId,
        IReadOnlySet<Guid> branchIds, DateTimeOffset from, DateTimeOffset to, Guid? branchId, string? currency,
        int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.ProfitSnapshots.AsNoTracking().Where(snapshot =>
            snapshot.OrganizationId == organizationId && branchIds.Contains(snapshot.BranchId)
            && (branchId == null || snapshot.BranchId == branchId)
            && (currency == null || snapshot.Currency == currency)
            && !dbContext.ProfitSnapshots.Any(next => next.OrganizationId == snapshot.OrganizationId
                && next.DealId == snapshot.DealId && next.Revision > snapshot.Revision)
            && dbContext.Deals.Any(deal => deal.OrganizationId == organizationId && deal.Id == snapshot.DealId
                && deal.CompletedAt >= from && deal.CompletedAt < to));
        var snapshots = await query.OrderByDescending(x => x.CreatedAt).Take(limit).ToListAsync(cancellationToken);
        var vehicleIds = snapshots.Select(x => x.VehicleId).Distinct().ToArray();
        var dealIds = snapshots.Select(x => x.DealId).Distinct().ToArray();
        var vehicles = await dbContext.Vehicles.AsNoTracking().Where(x => x.OrganizationId == organizationId
            && vehicleIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var deals = await dbContext.Deals.AsNoTracking().Where(x => x.OrganizationId == organizationId
            && dealIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        return snapshots.Select(snapshot =>
        {
            var vehicle = vehicles[snapshot.VehicleId]; var deal = deals[snapshot.DealId];
            return new FinanceDashboardRow(snapshot, $"{vehicle.Make} {vehicle.Model}", vehicle.Vin,
                vehicle.AcceptedAt, deal.CompletedAt!.Value);
        }).ToArray();
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        { throw new ConflictException("finance.version_conflict", "Финансовый источник изменён конкурентно."); }
        catch (Exception exception) when (FindPostgres(exception)?.SqlState is
                                              PostgresErrorCodes.UniqueViolation or
                                              PostgresErrorCodes.DeadlockDetected or
                                              PostgresErrorCodes.SerializationFailure)
        { throw new ConflictException("finance.constraint_conflict", "Финансовая команда конфликтует с сохранёнными данными."); }
        catch (DbUpdateException)
        { throw new ConflictException("finance.constraint_conflict", "Финансовая команда нарушает ограничения данных."); }
    }
    public void ResetTracking() => dbContext.ChangeTracker.Clear();
    private IQueryable<Deal> DealQuery() => dbContext.Deals.Include(x => x.Payments).Include(x => x.Documents)
        .Include(x => x.History).Include(x => x.Handover).AsSplitQuery();
    private static PostgresException? FindPostgres(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres; return null;
    }
}
