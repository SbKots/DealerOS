using System.Globalization;
using System.Text;
using System.Text.Json;
using DealerOS.Modules.Deals.Domain;
using DealerOS.Modules.Finance.Domain;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Vehicles.Application;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Finance.Application;

public sealed class FinanceService(IFinanceStore store, IAuditWriter audit, TimeProvider timeProvider)
{
    public async Task<VehicleEconomicsResponse> GetVehicleEconomicsAsync(ActorContext actor, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.FinanceView);
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        DemandBranch(actor, vehicle.BranchId);
        var rows = await store.ListDashboardAsync(actor.OrganizationId, actor.BranchIds,
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), timeProvider.GetUtcNow().AddDays(1),
            vehicle.BranchId, null, 1000, cancellationToken);
        var row = rows.SingleOrDefault(x => x.Snapshot.VehicleId == vehicleId)
            ?? throw new NotFoundException("Profit snapshot автомобиля не найден.");
        var revisions = await store.ListSnapshotsAsync(actor.OrganizationId, row.Snapshot.DealId,
            cancellationToken);
        var costs = await store.ListCostsAsync(actor.OrganizationId, row.Snapshot.DealId, cancellationToken);
        var supersededIds = costs.Where(x => x.SupersedesEntryId is not null)
            .Select(x => x.SupersedesEntryId!.Value).ToHashSet();
        return new VehicleEconomicsResponse(vehicle.Id, row.VehicleName, row.Vin, row.Snapshot.DealId,
            row.SoldAt, Map(revisions.OrderByDescending(x => x.Revision).First()), revisions.Select(Map).ToArray(),
            costs.Select(x => Map(x, supersededIds.Contains(x.Id))).ToArray());
    }

    public async Task<ManualCostResponse> CreateCostAsync(ActorContext actor, Guid dealId,
        CreateManualCostRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.FinanceEditCosts);
        var existing = await FindExistingAsync(actor, dealId, request.CostEntryId, request.CommandId,
            request.Category, request.Source, request.Reference, request.Amount, request.Currency,
            request.OccurredAt, null, null, cancellationToken);
        if (existing is not null) return Map(existing, false);
        var deal = await FindCompletedDealAsync(actor, dealId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (new Money(request.Amount, request.Currency).Currency != deal.Currency)
            throw new DomainException("finance.currency_mismatch", "Валюта расхода должна совпадать со сделкой.");
        var entry = ManualCostEntry.Create(request.CostEntryId, actor.OrganizationId, deal.BranchId,
            deal.VehicleId, deal.Id, request.CommandId, request.Category, request.Source ?? string.Empty,
            request.Reference ?? string.Empty, request.Amount, request.Currency ?? string.Empty,
            request.OccurredAt, request.Evidence, request.Comment, actor.UserId, now);
        await store.AddCostAsync(entry, cancellationToken);
        await store.CaptureAsync(deal, actor.UserId, "Manual cost added", now, cancellationToken);
        WriteAudit(actor, "finance.manual_cost_added", deal, entry.Id, correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return Map(entry, false);
    }

    public async Task<ManualCostResponse> CorrectCostAsync(ActorContext actor, Guid originalCostId,
        CorrectManualCostRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.FinanceEditCosts);
        var original = await store.FindCostAsync(actor.OrganizationId, originalCostId, cancellationToken)
            ?? throw new NotFoundException("Исходный расход не найден.");
        DemandBranch(actor, original.BranchId);
        var existing = await FindExistingAsync(actor, original.DealId, request.CostEntryId, request.CommandId,
            request.Category, request.Source, request.Reference, request.Amount, request.Currency,
            request.OccurredAt, original.Id, request.CorrectionReason, cancellationToken);
        if (existing is not null) return Map(existing, false);
        if (await store.IsSupersededAsync(actor.OrganizationId, original.Id, cancellationToken))
            throw new ConflictException("finance.cost_already_corrected", "У расхода уже есть correction.");
        var deal = await FindCompletedDealAsync(actor, original.DealId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (new Money(request.Amount, request.Currency).Currency != deal.Currency)
            throw new DomainException("finance.currency_mismatch", "Валюта correction должна совпадать со сделкой.");
        var entry = ManualCostEntry.Correct(request.CostEntryId, request.CommandId, original, request.Category,
            request.Source ?? string.Empty, request.Reference ?? string.Empty, request.Amount,
            request.Currency ?? string.Empty, request.OccurredAt, request.Evidence, request.Comment,
            request.CorrectionReason, actor.UserId, now);
        await store.AddCostAsync(entry, cancellationToken);
        await store.CaptureAsync(deal, actor.UserId, "Manual cost corrected", now, cancellationToken);
        WriteAudit(actor, "finance.manual_cost_corrected", deal, entry.Id, correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return Map(entry, false);
    }

    public async Task<FinanceDashboardResponse> GetDashboardAsync(ActorContext actor, DateTimeOffset? from,
        DateTimeOffset? to, Guid? branchId, string? currency, int limit, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.FinanceView);
        var end = to ?? timeProvider.GetUtcNow(); var start = from ?? end.AddDays(-90);
        if (start >= end || end - start > TimeSpan.FromDays(3660))
            throw new DomainException("finance.date_range_invalid", "Период dashboard недопустим.");
        if (branchId is not null) DemandBranch(actor, branchId.Value);
        var safeCurrency = string.IsNullOrWhiteSpace(currency) ? null : new Money(0, currency).Currency;
        var rows = await store.ListDashboardAsync(actor.OrganizationId, actor.BranchIds, start, end, branchId,
            safeCurrency, Math.Clamp(limit, 1, 1000), cancellationToken);
        var groups = rows.GroupBy(x => x.Snapshot.Currency).OrderBy(x => x.Key).Select(group =>
        {
            var vehicles = group.Select(ToDashboardItem).OrderByDescending(x => x.SoldAt).ToArray();
            var margins = vehicles.Where(x => x.ActualMarginPercent is not null).Select(x => x.ActualMarginPercent!.Value).ToArray();
            var days = vehicles.Where(x => x.DaysInStock is not null).Select(x => x.DaysInStock!.Value).ToArray();
            return new FinanceDashboardGroup(group.Key, vehicles.Length, vehicles.Sum(x => x.GrossRevenue),
                vehicles.Sum(x => x.NetRevenue), vehicles.Sum(x => x.TotalCost), vehicles.Sum(x => x.ActualProfit),
                margins.Length == 0 ? null : decimal.Round(margins.Average(), 2, MidpointRounding.ToEven),
                vehicles.Sum(x => x.ProfitVariance), days.Length == 0 ? null : decimal.Round((decimal)days.Average(), 2,
                    MidpointRounding.ToEven), vehicles, vehicles.Where(x => x.ActualProfit < 0).ToArray());
        }).ToArray();
        return new FinanceDashboardResponse(start, end, branchId, groups);
    }

    public async Task<FinanceCsvDownload> ExportCsvAsync(ActorContext actor, DateTimeOffset? from,
        DateTimeOffset? to, Guid? branchId, string? currency, string correlationId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.FinanceExport);
        var dashboard = await GetDashboardAsync(actor, from, to, branchId, currency, 1000, cancellationToken);
        var builder = new StringBuilder("VehicleId;VIN;DealId;BranchId;SoldAt;Currency;GrossRevenue;Refunds;NetRevenue;TotalCost;ActualProfit;ActualMarginPercent;PlanProfit;ProfitVariance\r\n");
        foreach (var item in dashboard.Groups.SelectMany(x => x.Vehicles))
        {
            var snapshot = (await store.ListSnapshotsAsync(actor.OrganizationId, item.DealId, cancellationToken))
                .OrderByDescending(x => x.Revision).First();
            builder.AppendJoin(';', item.VehicleId, Csv(item.Vin), item.DealId, item.BranchId,
                item.SoldAt.ToString("O"), item.Currency, Num(item.GrossRevenue), Num(snapshot.Refunds),
                Num(item.NetRevenue), Num(item.TotalCost), Num(item.ActualProfit),
                item.ActualMarginPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                Num(item.PlanProfit), Num(item.ProfitVariance)).Append("\r\n");
        }
        var now = timeProvider.GetUtcNow();
        audit.Write(actor.OrganizationId, actor.UserId, "finance.csv_exported", "FinanceExport", Guid.NewGuid(),
            null, JsonSerializer.Serialize(new
            {
                dashboard.From,
                dashboard.To,
                dashboard.BranchId,
                currencies = dashboard.Groups.Select(x => x.Currency).ToArray(),
                count = dashboard.Groups.Sum(x => x.SoldVehicles)
            }),
            correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return new FinanceCsvDownload(new UTF8Encoding(true).GetBytes(builder.ToString()),
            $"dealeros-sold-economics-{now:yyyyMMddHHmmss}.csv", "text/csv; charset=utf-8");
    }

    private async Task<ManualCostEntry?> FindExistingAsync(ActorContext actor, Guid dealId, Guid costId,
        Guid commandId, ManualCostCategory category, string? source, string? reference, decimal amount,
        string? currency, DateTimeOffset occurredAt, Guid? supersedesId, string? correctionReason,
        CancellationToken cancellationToken)
    {
        var byId = await store.FindCostAsync(actor.OrganizationId, costId, cancellationToken);
        var byCommand = await store.FindCostByCommandAsync(actor.OrganizationId, commandId, cancellationToken);
        if (byId is not null && byCommand is not null && byId.Id != byCommand.Id)
            throw new ConflictException("finance.cost_command_conflict", "Command ID относится к другому расходу.");
        var existing = byId ?? byCommand; if (existing is null) return null; DemandBranch(actor, existing.BranchId);
        if (!existing.Matches(dealId, category, source ?? string.Empty, reference ?? string.Empty, amount,
                currency ?? string.Empty, occurredAt, supersedesId, correctionReason, actor.UserId))
            throw new ConflictException("finance.cost_id_conflict", "Cost ID использован с другим payload.");
        return existing;
    }

    private async Task<Deal> FindCompletedDealAsync(ActorContext actor, Guid dealId,
        CancellationToken cancellationToken)
    {
        var deal = await store.FindDealAsync(actor.OrganizationId, dealId, cancellationToken)
            ?? throw new NotFoundException("Сделка не найдена."); DemandBranch(actor, deal.BranchId);
        if (deal.Status != DealStatus.Completed)
            throw new DomainException("finance.completed_deal_required", "Экономика фиксируется после Completed Deal.");
        return deal;
    }

    private static ProfitSnapshotResponse Map(ProfitSnapshot x) => new(x.Id, x.DealId, x.VehicleId, x.Revision,
        x.RevisesSnapshotId, x.Reason, x.FormulaVersion, x.Currency, x.GrossRevenue, x.Refunds, x.NetRevenue,
        x.PurchaseCost, x.OperationsCost, x.ManualCost, x.TotalCost, x.ActualProfit, x.ActualMarginPercent,
        x.PlanProfit, x.PlanMarginPercent, x.SourcesJson, x.Sha256, x.CreatedAt);
    private static ManualCostResponse Map(ManualCostEntry x, bool superseded) => new(x.Id, x.DealId, x.VehicleId,
        x.Category.ToString(), x.Source, x.Reference, x.Amount, x.Currency, x.OccurredAt, x.Evidence, x.Comment,
        x.SupersedesEntryId, x.CorrectionReason, x.RecordedAt, superseded);
    private static FinanceDashboardItem ToDashboardItem(FinanceDashboardRow row)
    {
        int? days = row.AcceptedAt is null ? null
            : Math.Max(0, (int)Math.Ceiling((row.SoldAt - row.AcceptedAt.Value).TotalDays));
        return new FinanceDashboardItem(row.Snapshot.VehicleId, row.VehicleName, row.Vin, row.Snapshot.DealId,
            row.Snapshot.BranchId, row.Snapshot.Currency, row.Snapshot.GrossRevenue, row.Snapshot.NetRevenue,
            row.Snapshot.TotalCost, row.Snapshot.ActualProfit, row.Snapshot.ActualMarginPercent,
            row.Snapshot.PlanProfit, row.Snapshot.ActualProfit - row.Snapshot.PlanProfit, days, row.SoldAt);
    }
    private void WriteAudit(ActorContext actor, string operation, Deal deal, Guid entryId, string correlationId,
        DateTimeOffset now) => audit.Write(actor.OrganizationId, actor.UserId, operation, "ManualCostEntry", entryId,
        null, JsonSerializer.Serialize(new { deal.Id, deal.VehicleId, deal.BranchId, deal.Currency }), correlationId, now);
    private static string Num(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static void Demand(ActorContext actor, string permission)
    { if (!actor.Permissions.Contains(permission)) throw new ForbiddenException("Недостаточно прав для экономики."); }
    private static void DemandBranch(ActorContext actor, Guid branchId)
    { if (!actor.BranchIds.Contains(branchId)) throw new ForbiddenException("Филиал недоступен пользователю."); }
}
