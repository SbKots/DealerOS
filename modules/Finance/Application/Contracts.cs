using DealerOS.Modules.Deals.Domain;
using DealerOS.Modules.Finance.Domain;
using DealerOS.Modules.Vehicles.Domain;

namespace DealerOS.Modules.Finance.Application;

public sealed record CreateManualCostRequest(Guid CostEntryId, Guid CommandId, ManualCostCategory Category,
    string? Source, string? Reference, decimal Amount, string? Currency, DateTimeOffset OccurredAt,
    string? Evidence, string? Comment);
public sealed record CorrectManualCostRequest(Guid CostEntryId, Guid CommandId, ManualCostCategory Category,
    string? Source, string? Reference, decimal Amount, string? Currency, DateTimeOffset OccurredAt,
    string? Evidence, string? Comment, string? CorrectionReason);
public sealed record ManualCostResponse(Guid Id, Guid DealId, Guid VehicleId, string Category, string Source,
    string Reference, decimal Amount, string Currency, DateTimeOffset OccurredAt, string? Evidence,
    string? Comment, Guid? SupersedesEntryId, string? CorrectionReason, DateTimeOffset RecordedAt,
    bool Superseded);
public sealed record ProfitSnapshotResponse(Guid Id, Guid DealId, Guid VehicleId, int Revision,
    Guid? RevisesSnapshotId, string Reason, string FormulaVersion, string Currency, decimal GrossRevenue,
    decimal Refunds, decimal NetRevenue, decimal PurchaseCost, decimal OperationsCost, decimal ManualCost,
    decimal TotalCost, decimal ActualProfit, decimal? ActualMarginPercent, decimal PlanProfit,
    decimal? PlanMarginPercent, string SourcesJson, string Sha256, DateTimeOffset CreatedAt);
public sealed record VehicleEconomicsResponse(Guid VehicleId, string VehicleName, string Vin, Guid DealId,
    DateTimeOffset? SoldAt, ProfitSnapshotResponse Latest, IReadOnlyList<ProfitSnapshotResponse> Revisions,
    IReadOnlyList<ManualCostResponse> ManualCosts);
public sealed record FinanceDashboardItem(Guid VehicleId, string VehicleName, string Vin, Guid DealId,
    Guid BranchId, string Currency, decimal GrossRevenue, decimal NetRevenue, decimal TotalCost,
    decimal ActualProfit, decimal? ActualMarginPercent, decimal PlanProfit, decimal ProfitVariance,
    int? DaysInStock, DateTimeOffset SoldAt);
public sealed record FinanceDashboardGroup(string Currency, int SoldVehicles, decimal GrossRevenue,
    decimal NetRevenue, decimal TotalCost, decimal ActualProfit, decimal? AverageMarginPercent,
    decimal PlanFactProfitVariance, decimal? AverageDaysInStock, IReadOnlyList<FinanceDashboardItem> Vehicles,
    IReadOnlyList<FinanceDashboardItem> LossVehicles);
public sealed record FinanceDashboardResponse(DateTimeOffset From, DateTimeOffset To, Guid? BranchId,
    IReadOnlyList<FinanceDashboardGroup> Groups);
public sealed record FinanceCsvDownload(byte[] Content, string FileName, string ContentType);
public sealed record FinanceDashboardRow(ProfitSnapshot Snapshot, string VehicleName, string Vin,
    DateTimeOffset? AcceptedAt, DateTimeOffset SoldAt);

public interface IFinanceStore
{
    Task<Deal?> FindDealAsync(Guid organizationId, Guid dealId, CancellationToken cancellationToken);
    Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<ManualCostEntry?> FindCostAsync(Guid organizationId, Guid costId, CancellationToken cancellationToken);
    Task<ManualCostEntry?> FindCostByCommandAsync(Guid organizationId, Guid commandId,
        CancellationToken cancellationToken);
    Task<bool> IsSupersededAsync(Guid organizationId, Guid costId, CancellationToken cancellationToken);
    Task AddCostAsync(ManualCostEntry entry, CancellationToken cancellationToken);
    Task CaptureAsync(Deal deal, Guid actorUserId, string reason, DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ProfitSnapshot>> ListSnapshotsAsync(Guid organizationId, Guid dealId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ManualCostEntry>> ListCostsAsync(Guid organizationId, Guid dealId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<FinanceDashboardRow>> ListDashboardAsync(Guid organizationId,
        IReadOnlySet<Guid> branchIds, DateTimeOffset from, DateTimeOffset to, Guid? branchId, string? currency,
        int limit, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    void ResetTracking();
}
