using System.Text.Json;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Sales.Domain;

public enum SalesOfferStatus
{
    Draft = 1, Submitted = 2, Approved = 3, Rejected = 4, ChangesRequested = 5,
    Expired = 6, Cancelled = 7
}
public enum OfferDecisionType { Approved = 1, Rejected = 2, ChangesRequested = 3 }
public sealed record OfferLineDraft(Guid Id, string Category, string Name, decimal Amount, string Currency);

public sealed class SalesOffer
{
    private readonly List<SalesOfferLineItem> _lineItems = [];
    private readonly List<SalesOfferHistory> _history = [];
    private readonly List<SalesOfferDecision> _decisions = [];
    private SalesOffer() { }
    private SalesOffer(Guid id, Guid organizationId, Guid branchId, Guid customerId, Guid leadId, Guid vehicleId,
        Guid createdByUserId, Guid? revisesOfferId, int revision, decimal basePriceAmount, decimal costSnapshotAmount,
        decimal minimumMarginAmount, string currency, DateTimeOffset validUntil, IEnumerable<OfferLineDraft> lines,
        decimal discountAmount, DateTimeOffset now)
    {
        Id = id; OrganizationId = organizationId; BranchId = branchId; CustomerId = customerId; LeadId = leadId;
        VehicleId = vehicleId; CreatedByUserId = createdByUserId; RevisesOfferId = revisesOfferId;
        Revision = revision; BasePriceAmount = Positive(basePriceAmount, currency, "Публичная цена должна быть положительной.");
        CostSnapshotAmount = NonNegative(costSnapshotAmount, currency);
        MinimumMarginAmount = NonNegative(minimumMarginAmount, currency); Currency = new Money(0, currency).Currency;
        ValidUntil = ValidateValidity(validUntil, now); Status = SalesOfferStatus.Draft; Version = 1;
        CreatedAt = now; UpdatedAt = now; ReplaceLines(lines); SetDiscount(discountAmount);
        CreateSignature = BuildCreateSignature(branchId, customerId, leadId, vehicleId, createdByUserId,
            validUntil, _lineItems.Select(x => new OfferLineDraft(x.Id, x.Category, x.Name, x.Amount, x.Currency)),
            DiscountAmount, BasePriceAmount, CostSnapshotAmount, MinimumMarginAmount, Currency);
        _history.Add(new SalesOfferHistory(Guid.NewGuid(), organizationId, id, Guid.NewGuid(), "Created", null,
            createdByUserId, now));
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? RevisesOfferId { get; private set; }
    public string CreateSignature { get; private set; } = string.Empty;
    public int Revision { get; private set; }
    public SalesOfferStatus Status { get; private set; }
    public decimal BasePriceAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal CostSnapshotAmount { get; private set; }
    public decimal MinimumMarginAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset ValidUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }
    public ApprovedOfferSnapshot? ApprovedSnapshot { get; private set; }
    public IReadOnlyCollection<SalesOfferLineItem> LineItems => _lineItems;
    public IReadOnlyCollection<SalesOfferHistory> History => _history;
    public IReadOnlyCollection<SalesOfferDecision> Decisions => _decisions;
    public decimal LineItemsAmount => _lineItems.Sum(x => x.Amount);
    public decimal FinalPriceAmount => BasePriceAmount + LineItemsAmount - DiscountAmount;
    public decimal ExpectedMarginAmount => FinalPriceAmount - CostSnapshotAmount;
    public bool IsBelowMinimumMargin => ExpectedMarginAmount < MinimumMarginAmount;

    public static SalesOffer Create(Guid id, Guid organizationId, Guid branchId, Guid customerId, Guid leadId,
        Guid vehicleId, Guid createdByUserId, decimal basePriceAmount, decimal costSnapshotAmount,
        decimal minimumMarginAmount, string currency, DateTimeOffset validUntil, IEnumerable<OfferLineDraft> lines,
        decimal discountAmount, DateTimeOffset now) => new(id, organizationId, branchId, customerId, leadId,
            vehicleId, createdByUserId, null, 1, basePriceAmount, costSnapshotAmount, minimumMarginAmount, currency,
            validUntil, lines, discountAmount, now);

    public bool MatchesCreate(Guid branchId, Guid customerId, Guid leadId, Guid vehicleId, Guid actorUserId,
        DateTimeOffset validUntil, IEnumerable<OfferLineDraft> lines, decimal discountAmount)
    {
        var normalizedLines = NormalizeLines(lines, Currency);
        var discount = NonNegative(discountAmount, Currency);
        return CreateSignature == BuildCreateSignature(branchId, customerId, leadId, vehicleId, actorUserId,
            validUntil, normalizedLines, discount, BasePriceAmount, CostSnapshotAmount, MinimumMarginAmount,
            Currency);
    }

    public static IReadOnlyList<OfferLineDraft> ValidateLines(IEnumerable<OfferLineDraft> lines, string currency) =>
        NormalizeLines(lines, new Money(0, currency).Currency);

    public bool Update(Guid commandId, IEnumerable<OfferLineDraft> lines, decimal discountAmount,
        DateTimeOffset validUntil, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        var normalized = NormalizeLines(lines, Currency);
        var discount = NonNegative(discountAmount, Currency);
        var valid = ValidateValidity(validUntil, now);
        var signature = JsonSerializer.Serialize(new { lines = normalized, discount, valid });
        if (EnsureCommand(commandId, "Updated", signature)) return false;
        EnsureVersion(expectedVersion); EnsureEditable();
        ReplaceLines(normalized); SetDiscount(discount); ValidUntil = valid;
        Record(commandId, "Updated", signature, actorUserId, now); return true;
    }

    public bool Submit(Guid commandId, bool autoApprovalAllowed, decimal autoApprovalDiscountLimit,
        long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        var signature = JsonSerializer.Serialize(new { autoApprovalAllowed, autoApprovalDiscountLimit });
        var existing = _history.SingleOrDefault(x => x.CommandId == commandId);
        if (existing is not null)
        {
            if (existing.Operation is not ("Submitted" or "AutoApproved") || existing.Signature != signature)
                throw new ConflictException("sales.offer_command_conflict", "Command ID offer использован с другим payload.");
            return false;
        }
        EnsureVersion(expectedVersion); EnsureEditable(); EnsureNotExpired(now); EnsureTotals();
        if (autoApprovalAllowed && DiscountAmount <= autoApprovalDiscountLimit && !IsBelowMinimumMargin)
        {
            Status = SalesOfferStatus.Approved;
            ApprovedSnapshot = BuildSnapshot(actorUserId, now);
            Record(commandId, "AutoApproved", signature, actorUserId, now);
        }
        else
        {
            Status = SalesOfferStatus.Submitted;
            Record(commandId, "Submitted", signature, actorUserId, now);
        }
        return true;
    }

    public bool Decide(Guid decisionId, OfferDecisionType type, string? reason, long expectedVersion,
        Guid actorUserId, DateTimeOffset now)
    {
        var normalized = Require(reason, 2000, "Причина решения обязательна.");
        var signature = JsonSerializer.Serialize(new { type, normalized });
        var existing = _decisions.SingleOrDefault(x => x.Id == decisionId);
        if (existing is not null)
        {
            if (existing.Type != type || existing.Reason != normalized || existing.ActorUserId != actorUserId)
                throw new ConflictException("sales.offer_decision_conflict", "Decision ID использован с другим payload.");
            return false;
        }
        EnsureVersion(expectedVersion); EnsureStatus(SalesOfferStatus.Submitted); EnsureNotExpired(now);
        Status = type switch
        {
            OfferDecisionType.Approved => SalesOfferStatus.Approved,
            OfferDecisionType.Rejected => SalesOfferStatus.Rejected,
            OfferDecisionType.ChangesRequested => SalesOfferStatus.ChangesRequested,
            _ => throw new DomainException("sales.invalid_offer_decision", "Недопустимое решение.")
        };
        _decisions.Add(new SalesOfferDecision(decisionId, OrganizationId, Id, type, normalized, actorUserId, now));
        if (type == OfferDecisionType.Approved) ApprovedSnapshot = BuildSnapshot(actorUserId, now);
        Record(decisionId, type.ToString(), signature, actorUserId, now); return true;
    }

    public SalesOffer CreateRevision(Guid commandId, Guid revisionId, long expectedVersion, Guid actorUserId,
        DateTimeOffset validUntil, DateTimeOffset now)
    {
        var signature = JsonSerializer.Serialize(new { revisionId, validUntil });
        if (EnsureCommand(commandId, "RevisionCreated", signature))
            throw new ConflictException("sales.revision_already_created", "Revision уже создана этой командой.");
        EnsureVersion(expectedVersion); EnsureStatus(SalesOfferStatus.Approved);
        Record(commandId, "RevisionCreated", signature, actorUserId, now);
        return new SalesOffer(revisionId, OrganizationId, BranchId, CustomerId, LeadId, VehicleId, actorUserId,
            Id, Revision + 1, BasePriceAmount, CostSnapshotAmount, MinimumMarginAmount, Currency, validUntil,
            _lineItems.Select(x => new OfferLineDraft(Guid.NewGuid(), x.Category, x.Name, x.Amount, x.Currency)),
            DiscountAmount, now);
    }

    public bool Expire(Guid commandId, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        if (EnsureCommand(commandId, "Expired", null)) return false;
        EnsureVersion(expectedVersion);
        if (Status is SalesOfferStatus.Approved or SalesOfferStatus.Rejected or SalesOfferStatus.Cancelled)
            throw new DomainException("sales.offer_final", "Финальное предложение нельзя истечь командой.");
        if (now <= ValidUntil) throw new DomainException("sales.offer_not_expired", "Срок предложения ещё не истёк.");
        Status = SalesOfferStatus.Expired; Record(commandId, "Expired", null, actorUserId, now); return true;
    }

    public bool Cancel(Guid commandId, string? reason, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        var normalized = Require(reason, 2000, "Причина отмены обязательна.");
        if (EnsureCommand(commandId, "Cancelled", normalized)) return false;
        EnsureVersion(expectedVersion);
        if (Status is SalesOfferStatus.Approved or SalesOfferStatus.Rejected or SalesOfferStatus.Expired
            or SalesOfferStatus.Cancelled)
            throw new DomainException("sales.offer_final", "Финальное предложение нельзя отменить.");
        Status = SalesOfferStatus.Cancelled; Record(commandId, "Cancelled", normalized, actorUserId, now);
        return true;
    }

    private ApprovedOfferSnapshot BuildSnapshot(Guid actorUserId, DateTimeOffset now) => new(Guid.NewGuid(),
        OrganizationId, Id, Revision, BasePriceAmount, LineItemsAmount, DiscountAmount, FinalPriceAmount,
        CostSnapshotAmount, ExpectedMarginAmount, MinimumMarginAmount, Currency, ValidUntil,
        JsonSerializer.Serialize(_lineItems.OrderBy(x => x.Id).Select(x => new
        {
            x.Category,
            x.Name,
            x.Amount,
            x.Currency
        })), actorUserId, now);
    private void ReplaceLines(IEnumerable<OfferLineDraft> lines)
    {
        var normalized = NormalizeLines(lines, Currency); _lineItems.Clear();
        foreach (var line in normalized)
            _lineItems.Add(new SalesOfferLineItem(line.Id, OrganizationId, Id, line.Category, line.Name,
                line.Amount, line.Currency));
    }
    private static IReadOnlyList<OfferLineDraft> NormalizeLines(IEnumerable<OfferLineDraft> lines, string currency)
    {
        var result = lines.Select(x => new OfferLineDraft(x.Id, Require(x.Category, 100, "Категория обязательна."),
            Require(x.Name, 300, "Название line item обязательно."), Positive(x.Amount, x.Currency,
                "Сумма line item должна быть положительной."), new Money(0, x.Currency).Currency)).ToArray();
        if (result.Select(x => x.Id).Distinct().Count() != result.Length)
            throw new DomainException("sales.duplicate_line_id", "Line item ID не должен повторяться.");
        if (result.Any(x => x.Currency != currency))
            throw new DomainException("sales.offer_currency_mismatch", "Валюта line item должна совпадать с offer.");
        return result;
    }
    private static string BuildCreateSignature(Guid branchId, Guid customerId, Guid leadId, Guid vehicleId,
        Guid actorUserId, DateTimeOffset validUntil, IEnumerable<OfferLineDraft> lines, decimal discountAmount,
        decimal basePriceAmount, decimal costSnapshotAmount, decimal minimumMarginAmount, string currency) =>
        JsonSerializer.Serialize(new
        {
            branchId,
            customerId,
            leadId,
            vehicleId,
            actorUserId,
            validUntil,
            lines = lines.OrderBy(x => x.Id).ToArray(),
            discountAmount,
            basePriceAmount,
            costSnapshotAmount,
            minimumMarginAmount,
            currency
        });
    private void SetDiscount(decimal discount)
    {
        DiscountAmount = NonNegative(discount, Currency);
        EnsureTotals();
    }
    private void EnsureTotals()
    {
        if (FinalPriceAmount <= 0)
            throw new DomainException("sales.offer_total_not_positive", "Итоговая цена должна быть положительной.");
        if (DiscountAmount > BasePriceAmount + LineItemsAmount)
            throw new DomainException("sales.discount_exceeds_total", "Скидка превышает сумму прозрачных компонентов.");
    }
    private bool EnsureCommand(Guid commandId, string operation, string? signature)
    {
        var existing = _history.SingleOrDefault(x => x.CommandId == commandId);
        if (existing is null) return false;
        if (existing.Operation != operation || existing.Signature != signature)
            throw new ConflictException("sales.offer_command_conflict", "Command ID offer использован с другим payload.");
        return true;
    }
    private void Record(Guid commandId, string operation, string? signature, Guid actorUserId, DateTimeOffset now)
    { _history.Add(new SalesOfferHistory(Guid.NewGuid(), OrganizationId, Id, commandId, operation, signature, actorUserId, now)); UpdatedAt = now; Version++; }
    private void EnsureVersion(long expectedVersion)
    { if (Version != expectedVersion) throw new ConflictException("sales.offer_version_conflict", "Offer изменён конкурентно."); }
    private void EnsureEditable()
    { if (Status is not (SalesOfferStatus.Draft or SalesOfferStatus.ChangesRequested)) throw new DomainException("sales.offer_immutable", "Offer недоступен для редактирования."); }
    private void EnsureStatus(SalesOfferStatus status)
    { if (Status != status) throw new DomainException("sales.offer_status_conflict", $"Команда недоступна в статусе {Status}."); }
    private void EnsureNotExpired(DateTimeOffset now)
    { if (now > ValidUntil) throw new DomainException("sales.offer_expired", "Срок предложения истёк."); }
    private static DateTimeOffset ValidateValidity(DateTimeOffset value, DateTimeOffset now)
    { if (value <= now || value > now.AddDays(90)) throw new DomainException("sales.invalid_offer_validity", "Срок действия должен быть в пределах 90 дней."); return value; }
    private static decimal Positive(decimal value, string? currency, string message)
    { var amount = new Money(value, currency).Amount; if (amount <= 0) throw new DomainException("sales.positive_amount_required", message); return amount; }
    private static decimal NonNegative(decimal value, string? currency) => new Money(value, currency).Amount;
    private static string Require(string? value, int max, string message)
    { if (string.IsNullOrWhiteSpace(value)) throw new DomainException("sales.required", message); var normalized = value.Trim(); if (normalized.Length > max) throw new DomainException("sales.too_long", $"Максимальная длина — {max} символов."); return normalized; }
}

public sealed class SalesOfferLineItem
{
    private SalesOfferLineItem() { }
    internal SalesOfferLineItem(Guid id, Guid organizationId, Guid offerId, string category, string name,
        decimal amount, string currency)
    { Id = id; OrganizationId = organizationId; OfferId = offerId; Category = category; Name = name; Amount = amount; Currency = currency; }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid OfferId { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
}

public sealed class SalesOfferHistory
{
    private SalesOfferHistory() { }
    internal SalesOfferHistory(Guid id, Guid organizationId, Guid offerId, Guid commandId, string operation,
        string? signature, Guid actorUserId, DateTimeOffset occurredAt)
    { Id = id; OrganizationId = organizationId; OfferId = offerId; CommandId = commandId; Operation = operation; Signature = signature; ActorUserId = actorUserId; OccurredAt = occurredAt; }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid OfferId { get; private set; }
    public Guid CommandId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string? Signature { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

public sealed class SalesOfferDecision
{
    private SalesOfferDecision() { }
    internal SalesOfferDecision(Guid id, Guid organizationId, Guid offerId, OfferDecisionType type, string reason,
        Guid actorUserId, DateTimeOffset occurredAt)
    { Id = id; OrganizationId = organizationId; OfferId = offerId; Type = type; Reason = reason; ActorUserId = actorUserId; OccurredAt = occurredAt; }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid OfferId { get; private set; }
    public OfferDecisionType Type { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

public sealed class ApprovedOfferSnapshot
{
    private ApprovedOfferSnapshot() { }
    internal ApprovedOfferSnapshot(Guid id, Guid organizationId, Guid offerId, int revision,
        decimal basePriceAmount, decimal lineItemsAmount, decimal discountAmount, decimal finalPriceAmount,
        decimal costSnapshotAmount, decimal expectedMarginAmount, decimal minimumMarginAmount, string currency,
        DateTimeOffset validUntil, string lineItemsJson, Guid approvedByUserId, DateTimeOffset approvedAt)
    { Id = id; OrganizationId = organizationId; OfferId = offerId; Revision = revision; BasePriceAmount = basePriceAmount; LineItemsAmount = lineItemsAmount; DiscountAmount = discountAmount; FinalPriceAmount = finalPriceAmount; CostSnapshotAmount = costSnapshotAmount; ExpectedMarginAmount = expectedMarginAmount; MinimumMarginAmount = minimumMarginAmount; Currency = currency; ValidUntil = validUntil; LineItemsJson = lineItemsJson; ApprovedByUserId = approvedByUserId; ApprovedAt = approvedAt; }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid OfferId { get; private set; }
    public int Revision { get; private set; }
    public decimal BasePriceAmount { get; private set; }
    public decimal LineItemsAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal FinalPriceAmount { get; private set; }
    public decimal CostSnapshotAmount { get; private set; }
    public decimal ExpectedMarginAmount { get; private set; }
    public decimal MinimumMarginAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset ValidUntil { get; private set; }
    public string LineItemsJson { get; private set; } = "[]";
    public Guid ApprovedByUserId { get; private set; }
    public DateTimeOffset ApprovedAt { get; private set; }
}
