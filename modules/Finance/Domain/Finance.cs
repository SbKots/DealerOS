using System.Text.Json;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Finance.Domain;

public enum ManualCostCategory
{
    Logistics = 1,
    DutiesAndFees = 2,
    Diagnostics = 3,
    Detailing = 4,
    Storage = 5,
    Advertising = 6,
    PartnerCommission = 7,
    DealVariableCost = 8,
    Other = 9
}

public sealed record CostSourceAmount(decimal Amount, string Currency);

public sealed record ProfitCalculation(decimal GrossRevenue, decimal Refunds, decimal NetRevenue,
    decimal PurchaseCost, decimal OperationsCost, decimal ManualCost, decimal TotalCost,
    decimal ActualProfit, decimal? ActualMarginPercent, decimal PlanProfit, decimal? PlanMarginPercent);

public static class ProfitCalculator
{
    public static ProfitCalculation Calculate(string currency, decimal grossRevenue, decimal refunds,
        decimal purchaseCost, IEnumerable<CostSourceAmount> operations,
        IEnumerable<CostSourceAmount> manualCosts, decimal planProfit)
    {
        var expectedCurrency = new Money(0, currency).Currency;
        var operationSources = operations.ToArray();
        var manualSources = manualCosts.ToArray();
        var mismatches = operationSources.Concat(manualSources)
            .Select(x => new Money(x.Amount, x.Currency).Currency)
            .Where(x => !string.Equals(x, expectedCurrency, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (mismatches.Length > 0)
            throw new DomainException("finance.mixed_currency",
                $"Расчёт заблокирован: ожидалась {expectedCurrency}, обнаружены {string.Join(", ", mismatches)}.");

        var gross = NonNegative(grossRevenue);
        var refundTotal = NonNegative(refunds);
        if (refundTotal > gross)
            throw new DomainException("finance.refunds_exceed_revenue", "Возвраты не могут превышать выручку.");
        var purchase = NonNegative(purchaseCost);
        var operationTotal = Sum(operationSources);
        var manualTotal = Sum(manualSources);
        var net = Round(gross - refundTotal);
        var totalCost = Round(purchase + operationTotal + manualTotal);
        var actualProfit = Signed(net - totalCost);
        var plannedProfit = Signed(planProfit);
        decimal? actualMargin = net > 0 ? Round(actualProfit / net * 100) : null;
        decimal? planMargin = gross > 0 ? Round(plannedProfit / gross * 100) : null;
        return new ProfitCalculation(gross, refundTotal, net, purchase, operationTotal, manualTotal,
            totalCost, actualProfit, actualMargin, plannedProfit, planMargin);
    }

    private static decimal Sum(IEnumerable<CostSourceAmount> sources) =>
        Round(sources.Sum(x => NonNegative(x.Amount)));
    private static decimal NonNegative(decimal value) => new Money(value, "RUB").Amount;
    private static decimal Signed(decimal value)
    {
        var rounded = Round(value);
        if (Math.Abs(rounded) > Money.MaxAmount)
            throw new DomainException("finance.amount_too_large", "Сумма превышает допустимый диапазон.");
        return rounded;
    }
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.ToEven);
}

public sealed class ManualCostEntry
{
    private ManualCostEntry() { }
    private ManualCostEntry(Guid id, Guid organizationId, Guid branchId, Guid vehicleId, Guid dealId,
        Guid commandId, ManualCostCategory category, string source, string reference, decimal amount,
        string currency, DateTimeOffset occurredAt, string? evidence, string? comment, Guid? supersedesEntryId,
        string? correctionReason, Guid actorUserId, DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty || commandId == Guid.Empty)
            throw new DomainException("finance.identifier_required", "Cost entry ID и command ID обязательны.");
        if (!Enum.IsDefined(category)) throw new DomainException("finance.cost_category_invalid", "Категория расхода недопустима.");
        var money = new Money(amount, currency);
        if (money.Amount <= 0) throw new DomainException("finance.cost_positive", "Расход должен быть положительным.");
        if (occurredAt > recordedAt.AddMinutes(5) || occurredAt < recordedAt.AddYears(-10))
            throw new DomainException("finance.cost_date_invalid", "Дата расхода находится вне допустимого диапазона.");
        Id = id; OrganizationId = organizationId; BranchId = branchId; VehicleId = vehicleId; DealId = dealId;
        CommandId = commandId; Category = category; Source = Require(source, 100, "Источник расхода обязателен.");
        Reference = Require(reference, 200, "Reference расхода обязателен."); Amount = money.Amount;
        Currency = money.Currency; OccurredAt = occurredAt; Evidence = Normalize(evidence, 1000);
        Comment = Normalize(comment, 2000); SupersedesEntryId = supersedesEntryId;
        CorrectionReason = supersedesEntryId is null ? null
            : Require(correctionReason, 2000, "Причина correction обязательна.");
        ActorUserId = actorUserId; RecordedAt = recordedAt;
        Signature = BuildSignature(dealId, category, Source, Reference, Amount, Currency, OccurredAt,
            SupersedesEntryId, CorrectionReason);
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid DealId { get; private set; }
    public Guid CommandId { get; private set; }
    public string Signature { get; private set; } = string.Empty;
    public ManualCostCategory Category { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Evidence { get; private set; }
    public string? Comment { get; private set; }
    public Guid? SupersedesEntryId { get; private set; }
    public string? CorrectionReason { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public static ManualCostEntry Create(Guid id, Guid organizationId, Guid branchId, Guid vehicleId, Guid dealId,
        Guid commandId, ManualCostCategory category, string source, string reference, decimal amount,
        string currency, DateTimeOffset occurredAt, string? evidence, string? comment, Guid actorUserId,
        DateTimeOffset recordedAt) => new(id, organizationId, branchId, vehicleId, dealId, commandId, category,
        source, reference, amount, currency, occurredAt, evidence, comment, null, null, actorUserId, recordedAt);

    public static ManualCostEntry Correct(Guid id, Guid commandId, ManualCostEntry original,
        ManualCostCategory category, string source, string reference, decimal amount, string currency,
        DateTimeOffset occurredAt, string? evidence, string? comment, string? correctionReason, Guid actorUserId,
        DateTimeOffset recordedAt) => new(id, original.OrganizationId, original.BranchId, original.VehicleId,
        original.DealId, commandId, category, source, reference, amount, currency, occurredAt, evidence, comment,
        original.Id, correctionReason, actorUserId, recordedAt);

    public bool Matches(Guid dealId, ManualCostCategory category, string source, string reference, decimal amount,
        string currency, DateTimeOffset occurredAt, Guid? supersedesId, string? correctionReason,
        Guid actorUserId) => ActorUserId == actorUserId && Signature == BuildSignature(dealId, category,
        source.Trim(), reference.Trim(), new Money(amount, currency).Amount, new Money(0, currency).Currency,
        occurredAt, supersedesId, supersedesId is null ? null : correctionReason?.Trim());

    private static string BuildSignature(Guid dealId, ManualCostCategory category, string source, string reference,
        decimal amount, string currency, DateTimeOffset occurredAt, Guid? supersedesId, string? correctionReason) =>
        JsonSerializer.Serialize(new
        {
            dealId,
            category,
            source,
            reference,
            amount,
            currency,
            occurredAt,
            supersedesId,
            correctionReason
        });
    private static string Require(string? value, int max, string message)
    { if (string.IsNullOrWhiteSpace(value)) throw new DomainException("finance.required", message); var safe = value.Trim(); if (safe.Length > max) throw new DomainException("finance.too_long", $"Максимальная длина — {max} символов."); return safe; }
    private static string? Normalize(string? value, int max)
    { if (string.IsNullOrWhiteSpace(value)) return null; var safe = value.Trim(); if (safe.Length > max) throw new DomainException("finance.too_long", $"Максимальная длина — {max} символов."); return safe; }
}

public sealed class ProfitSnapshot
{
    private ProfitSnapshot() { }
    private ProfitSnapshot(Guid id, Guid organizationId, Guid branchId, Guid dealId, Guid vehicleId, int revision,
        Guid? revisesSnapshotId, string reason, string formulaVersion, string currency, decimal grossRevenue,
        decimal refunds, decimal netRevenue, decimal purchaseCost, decimal operationsCost, decimal manualCost,
        decimal totalCost, decimal actualProfit, decimal? actualMarginPercent, decimal planProfit,
        decimal? planMarginPercent, string sourcesJson, string sha256, Guid createdByUserId, DateTimeOffset createdAt)
    {
        Id = id; OrganizationId = organizationId; BranchId = branchId; DealId = dealId; VehicleId = vehicleId;
        Revision = revision; RevisesSnapshotId = revisesSnapshotId; Reason = Require(reason, 2000);
        FormulaVersion = Require(formulaVersion, 50); Currency = new Money(0, currency).Currency;
        GrossRevenue = MoneyValue(grossRevenue); Refunds = MoneyValue(refunds); NetRevenue = MoneyValue(netRevenue);
        PurchaseCost = MoneyValue(purchaseCost); OperationsCost = MoneyValue(operationsCost);
        ManualCost = MoneyValue(manualCost); TotalCost = MoneyValue(totalCost);
        ActualProfit = SignedMoney(actualProfit); ActualMarginPercent = Percent(actualMarginPercent);
        PlanProfit = SignedMoney(planProfit); PlanMarginPercent = Percent(planMarginPercent);
        SourcesJson = string.IsNullOrWhiteSpace(sourcesJson) ? throw new DomainException("finance.sources_required", "Источники расчёта обязательны.") : sourcesJson;
        Sha256 = sha256 is { Length: 64 } ? sha256 : throw new DomainException("finance.hash_invalid", "SHA-256 snapshot недопустим.");
        CreatedByUserId = createdByUserId; CreatedAt = createdAt;
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid DealId { get; private set; }
    public Guid VehicleId { get; private set; }
    public int Revision { get; private set; }
    public Guid? RevisesSnapshotId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string FormulaVersion { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public decimal GrossRevenue { get; private set; }
    public decimal Refunds { get; private set; }
    public decimal NetRevenue { get; private set; }
    public decimal PurchaseCost { get; private set; }
    public decimal OperationsCost { get; private set; }
    public decimal ManualCost { get; private set; }
    public decimal TotalCost { get; private set; }
    public decimal ActualProfit { get; private set; }
    public decimal? ActualMarginPercent { get; private set; }
    public decimal PlanProfit { get; private set; }
    public decimal? PlanMarginPercent { get; private set; }
    public string SourcesJson { get; private set; } = "{}";
    public string Sha256 { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static ProfitSnapshot Create(Guid id, Guid organizationId, Guid branchId, Guid dealId, Guid vehicleId,
        int revision, Guid? revisesSnapshotId, string reason, string formulaVersion, string currency,
        decimal grossRevenue, decimal refunds, decimal netRevenue, decimal purchaseCost, decimal operationsCost,
        decimal manualCost, decimal totalCost, decimal actualProfit, decimal? actualMarginPercent,
        decimal planProfit, decimal? planMarginPercent, string sourcesJson, string sha256, Guid createdByUserId,
        DateTimeOffset createdAt) => new(id, organizationId, branchId, dealId, vehicleId, revision,
        revisesSnapshotId, reason, formulaVersion, currency, grossRevenue, refunds, netRevenue, purchaseCost,
        operationsCost, manualCost, totalCost, actualProfit, actualMarginPercent, planProfit, planMarginPercent,
        sourcesJson, sha256, createdByUserId, createdAt);

    private static decimal MoneyValue(decimal value) => new Money(value, "RUB").Amount;
    private static decimal SignedMoney(decimal value)
    { var safe = decimal.Round(value, 2, MidpointRounding.ToEven); if (Math.Abs(safe) > Money.MaxAmount) throw new DomainException("finance.amount_too_large", "Сумма превышает допустимый диапазон."); return safe; }
    private static decimal? Percent(decimal? value) => value is null ? null : decimal.Round(value.Value, 2, MidpointRounding.ToEven);
    private static string Require(string? value, int max)
    { if (string.IsNullOrWhiteSpace(value)) throw new DomainException("finance.required", "Обязательное значение отсутствует."); var safe = value.Trim(); if (safe.Length > max) throw new DomainException("finance.too_long", $"Максимальная длина — {max} символов."); return safe; }
}
