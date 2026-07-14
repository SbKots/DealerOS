using System.Text.Json;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Deals.Domain;

public enum DealStatus
{
    Draft = 1,
    AwaitingPayment = 2,
    ReadyForHandover = 3,
    Completed = 4,
    Cancelled = 5,
    RefundPending = 6,
    Refunded = 7
}

public enum PaymentKind { Deposit = 1, Payment = 2, Refund = 3, Adjustment = 4 }
public enum PaymentStatus { Pending = 1, Received = 2, Failed = 3, Cancelled = 4, Refunded = 5 }
public enum DealDocumentType { SaleContract = 1, HandoverAct = 2 }

public sealed class Deal
{
    private readonly List<DealHistory> _history = [];
    private readonly List<DealPayment> _payments = [];
    private readonly List<DealDocument> _documents = [];
    private Deal() { }

    private Deal(Guid id, Guid organizationId, Guid branchId, Guid reservationId, Guid approvedOfferSnapshotId,
        Guid customerId, Guid leadId, Guid vehicleId, Guid createdByUserId, Guid createCommandId,
        string customerNameSnapshot, string vehicleSnapshotJson, string lineItemsJson, decimal basePriceAmount,
        decimal lineItemsAmount, decimal discountAmount, decimal finalTotalAmount, decimal costSnapshotAmount,
        decimal expectedMarginAmount, string currency, bool depositReceived, decimal depositAmount,
        string? depositReference, DateTimeOffset now)
    {
        if (id == Guid.Empty || createCommandId == Guid.Empty || reservationId == Guid.Empty
            || approvedOfferSnapshotId == Guid.Empty)
            throw new DomainException("deal.identifier_required", "Идентификаторы сделки, брони и команды обязательны.");
        CustomerNameSnapshot = Require(customerNameSnapshot, 300, "Snapshot клиента обязателен.");
        VehicleSnapshotJson = Require(vehicleSnapshotJson, 4000, "Snapshot автомобиля обязателен.");
        LineItemsJson = string.IsNullOrWhiteSpace(lineItemsJson) ? "[]" : lineItemsJson;
        Currency = new Money(0, currency).Currency;
        BasePriceAmount = Positive(basePriceAmount, Currency, "Базовая цена должна быть положительной.");
        LineItemsAmount = NonNegative(lineItemsAmount, Currency);
        DiscountAmount = NonNegative(discountAmount, Currency);
        FinalTotalAmount = Positive(finalTotalAmount, Currency, "Итог сделки должен быть положительным.");
        CostSnapshotAmount = NonNegative(costSnapshotAmount, Currency);
        ExpectedMarginAmount = decimal.Round(expectedMarginAmount, 2, MidpointRounding.ToEven);
        if (Math.Abs(ExpectedMarginAmount) > Money.MaxAmount)
            throw new DomainException("deal.margin_too_large", "Snapshot маржи превышает допустимый диапазон.");

        Id = id; OrganizationId = organizationId; BranchId = branchId; ReservationId = reservationId;
        ApprovedOfferSnapshotId = approvedOfferSnapshotId; CustomerId = customerId; LeadId = leadId;
        VehicleId = vehicleId; CreatedByUserId = createdByUserId; CreateCommandId = createCommandId;
        Status = DealStatus.Draft; CreatedAt = now; UpdatedAt = now; Version = 1;
        CreateSignature = BuildCreateSignature(reservationId, approvedOfferSnapshotId, finalTotalAmount, Currency);
        _history.Add(new DealHistory(Guid.NewGuid(), organizationId, id, createCommandId, "Created",
            CreateSignature, createdByUserId, now));

        if (depositReceived)
        {
            var amount = Positive(depositAmount, Currency, "Полученная предоплата должна быть положительной.");
            if (amount > FinalTotalAmount)
                throw new DomainException("deal.deposit_exceeds_total", "Предоплата превышает итог сделки.");
            _payments.Add(new DealPayment(reservationId, organizationId, id, reservationId, PaymentKind.Deposit,
                PaymentStatus.Received, amount, Currency, NormalizeOptional(depositReference, 200)
                    ?? $"reservation:{reservationId:N}", "Перенесено из брони без повторного признания выручки",
                createdByUserId, now, now));
        }
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ReservationId { get; private set; }
    public Guid ApprovedOfferSnapshotId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid CreateCommandId { get; private set; }
    public string CreateSignature { get; private set; } = string.Empty;
    public string CustomerNameSnapshot { get; private set; } = string.Empty;
    public string VehicleSnapshotJson { get; private set; } = string.Empty;
    public string LineItemsJson { get; private set; } = "[]";
    public decimal BasePriceAmount { get; private set; }
    public decimal LineItemsAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal FinalTotalAmount { get; private set; }
    public decimal CostSnapshotAmount { get; private set; }
    public decimal ExpectedMarginAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DealStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? ClosureReason { get; private set; }
    public long Version { get; private set; }
    public DealHandoverSnapshot? Handover { get; private set; }
    public IReadOnlyCollection<DealHistory> History => _history;
    public IReadOnlyCollection<DealPayment> Payments => _payments;
    public IReadOnlyCollection<DealDocument> Documents => _documents;
    public decimal ReceivedTotal => _payments.Where(x => x.Status == PaymentStatus.Received
        && x.Kind is PaymentKind.Deposit or PaymentKind.Payment or PaymentKind.Adjustment).Sum(x => x.Amount);
    public decimal RefundedTotal => _payments.Where(x => x.Kind == PaymentKind.Refund
        && x.Status == PaymentStatus.Refunded).Sum(x => x.Amount);
    public decimal NetPaid => ReceivedTotal - RefundedTotal;
    public decimal Balance => FinalTotalAmount - NetPaid;

    public static Deal Create(Guid id, Guid organizationId, Guid branchId, Guid reservationId,
        Guid approvedOfferSnapshotId, Guid customerId, Guid leadId, Guid vehicleId, Guid createdByUserId,
        Guid createCommandId, string customerNameSnapshot, string vehicleSnapshotJson, string lineItemsJson,
        decimal basePriceAmount, decimal lineItemsAmount, decimal discountAmount, decimal finalTotalAmount,
        decimal costSnapshotAmount, decimal expectedMarginAmount, string currency, bool depositReceived,
        decimal depositAmount, string? depositReference, DateTimeOffset now) => new(id, organizationId, branchId,
        reservationId, approvedOfferSnapshotId, customerId, leadId, vehicleId, createdByUserId, createCommandId,
        customerNameSnapshot, vehicleSnapshotJson, lineItemsJson, basePriceAmount, lineItemsAmount, discountAmount,
        finalTotalAmount, costSnapshotAmount, expectedMarginAmount, currency, depositReceived, depositAmount,
        depositReference, now);

    public bool MatchesCreate(Guid reservationId, Guid approvedOfferSnapshotId, decimal finalTotalAmount,
        string currency) => CreateSignature == BuildCreateSignature(reservationId, approvedOfferSnapshotId,
        new Money(finalTotalAmount, currency).Amount, new Money(0, currency).Currency);

    public bool BeginPayment(Guid commandId, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        if (EnsureCommand(commandId, "AwaitingPayment", null)) return false;
        EnsureVersion(expectedVersion); EnsureStatus(DealStatus.Draft);
        Status = DealStatus.AwaitingPayment; Record(commandId, "AwaitingPayment", null, actorUserId, now);
        return true;
    }

    public bool RegisterPayment(Guid paymentId, Guid commandId, PaymentKind kind, PaymentStatus status,
        decimal amount, string? currency, string? manualReference, string? reason, DateTimeOffset occurredAt,
        long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        if (paymentId == Guid.Empty || commandId == Guid.Empty)
            throw new DomainException("deal.payment_identifier_required", "Payment ID и command ID обязательны.");
        var safeAmount = Positive(amount, currency, "Сумма платежа должна быть положительной.");
        var safeCurrency = new Money(0, currency).Currency;
        if (safeCurrency != Currency) throw new DomainException("deal.payment_currency_mismatch", "Валюта платежа должна совпадать со сделкой.");
        var reference = Require(manualReference, 200, "Manual reference платежа обязателен.");
        var normalizedReason = Require(reason, 2000, "Причина финансовой записи обязательна.");
        var existing = _payments.SingleOrDefault(x => x.Id == paymentId || x.CommandId == commandId
            || x.ManualReference == reference);
        if (existing is not null)
        {
            if (!existing.Matches(paymentId, commandId, kind, status, safeAmount, safeCurrency, reference,
                    normalizedReason, actorUserId))
                throw new ConflictException("deal.payment_idempotency_conflict",
                    "Payment ID, command ID или manual reference использован с другим payload.");
            return false;
        }
        EnsureVersion(expectedVersion);
        if (occurredAt > now.AddMinutes(5) || occurredAt < CreatedAt.AddDays(-1))
            throw new DomainException("deal.payment_time_invalid", "Время платежа находится вне допустимого диапазона.");
        if (kind == PaymentKind.Deposit)
            throw new DomainException("deal.deposit_transfer_only", "Deposit переносится только из брони.");
        var closesCancelledDeal = false;
        if (kind == PaymentKind.Refund)
        {
            if (Status is not (DealStatus.RefundPending or DealStatus.Completed))
                throw new DomainException("deal.status_conflict", $"Возврат недоступен в статусе {Status}.");
            closesCancelledDeal = Status == DealStatus.RefundPending;
            if (status != PaymentStatus.Refunded)
                throw new DomainException("deal.refund_status_invalid", "Завершённый возврат должен иметь статус Refunded.");
            if (safeAmount > NetPaid)
                throw new DomainException("deal.refund_exceeds_paid", "Возврат превышает полученную сумму.");
        }
        else
        {
            EnsureStatus(DealStatus.AwaitingPayment);
            if (status == PaymentStatus.Refunded)
                throw new DomainException("deal.payment_status_invalid", "Обычный платёж не может иметь статус Refunded.");
            if (status == PaymentStatus.Received && NetPaid + safeAmount > FinalTotalAmount)
                throw new DomainException("deal.overpayment", "Платёж создаёт неподтверждённую переплату.");
        }
        _payments.Add(new DealPayment(paymentId, OrganizationId, Id, commandId, kind, status, safeAmount,
            safeCurrency, reference, normalizedReason, actorUserId, occurredAt, now));
        if (kind == PaymentKind.Refund && NetPaid == 0 && closesCancelledDeal)
        {
            Status = DealStatus.Refunded; ClosedAt = now;
        }
        Record(commandId, kind == PaymentKind.Refund ? "RefundRegistered" : "PaymentRegistered",
            JsonSerializer.Serialize(new { paymentId, kind, status, safeAmount, safeCurrency, reference }),
            actorUserId, now);
        return true;
    }

    public bool AddDocument(Guid documentId, Guid commandId, DealDocumentType type, string number,
        string templateName, int templateVersion, string sha256, string objectKey, long sizeBytes, string? reason,
        long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        if (documentId == Guid.Empty || commandId == Guid.Empty)
            throw new DomainException("deal.document_identifier_required", "Document ID и command ID обязательны.");
        var safeNumber = Require(number, 100, "Номер документа обязателен.");
        var safeTemplate = Require(templateName, 100, "Шаблон документа обязателен.");
        var safeHash = Require(sha256, 64, "SHA-256 обязателен.");
        var safeObjectKey = Require(objectKey, 1000, "Object key обязателен.");
        if (templateVersion <= 0 || sizeBytes <= 0)
            throw new DomainException("deal.document_metadata_invalid", "Версия шаблона и размер должны быть положительными.");
        var existing = _documents.SingleOrDefault(x => x.Id == documentId || x.CommandId == commandId);
        if (existing is not null)
        {
            if (!existing.Matches(documentId, commandId, type, safeHash, actorUserId))
                throw new ConflictException("deal.document_idempotency_conflict", "Document ID использован с другим payload.");
            return false;
        }
        EnsureVersion(expectedVersion);
        if (Status is not (DealStatus.Draft or DealStatus.AwaitingPayment))
            throw new DomainException("deal.document_generation_closed", "Документы формируются до этапа выдачи.");
        var previous = _documents.Where(x => x.Type == type).OrderByDescending(x => x.Revision).FirstOrDefault();
        var normalizedReason = previous is null ? NormalizeOptional(reason, 2000)
            : Require(reason, 2000, "Причина новой ревизии документа обязательна.");
        _documents.Add(new DealDocument(documentId, OrganizationId, Id, commandId, type, safeNumber,
            safeTemplate, templateVersion, previous?.Revision + 1 ?? 1, previous?.Id, safeHash, safeObjectKey,
            sizeBytes, normalizedReason, actorUserId, now));
        Record(commandId, "DocumentGenerated", JsonSerializer.Serialize(new { documentId, type, safeHash }),
            actorUserId, now);
        return true;
    }

    public bool MarkReadyForHandover(Guid commandId, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        if (EnsureCommand(commandId, "ReadyForHandover", null)) return false;
        EnsureVersion(expectedVersion); EnsureStatus(DealStatus.AwaitingPayment);
        if (NetPaid != FinalTotalAmount)
            throw new DomainException("deal.payment_incomplete", "Для выдачи требуется полная подтверждённая оплата.");
        if (!HasRequiredDocuments())
            throw new DomainException("deal.documents_incomplete", "Для выдачи нужны договор и акт.");
        Status = DealStatus.ReadyForHandover;
        Record(commandId, "ReadyForHandover", null, actorUserId, now); return true;
    }

    public bool CompleteHandover(Guid commandId, int actualMileageKm, bool keysTransferred,
        bool documentsTransferred, bool equipmentTransferred, bool conditionConfirmed, bool issuerConfirmed,
        bool responsibleConfirmed, string? conditionNotes, string? comments, long expectedVersion,
        Guid actorUserId, DateTimeOffset now)
    {
        var signature = JsonSerializer.Serialize(new
        {
            actualMileageKm,
            keysTransferred,
            documentsTransferred,
            equipmentTransferred,
            conditionConfirmed,
            issuerConfirmed,
            responsibleConfirmed,
            conditionNotes,
            comments
        });
        if (EnsureCommand(commandId, "HandoverCompleted", signature)) return false;
        EnsureVersion(expectedVersion); EnsureStatus(DealStatus.ReadyForHandover);
        if (actualMileageKm < 0 || actualMileageKm > 3_000_000)
            throw new DomainException("deal.handover_mileage_invalid", "Пробег выдачи недопустим.");
        if (!keysTransferred || !documentsTransferred || !equipmentTransferred || !conditionConfirmed
            || !issuerConfirmed || !responsibleConfirmed)
            throw new DomainException("deal.handover_checklist_incomplete", "Все обязательные пункты выдачи должны быть подтверждены.");
        Handover = new DealHandoverSnapshot(Guid.NewGuid(), OrganizationId, Id, actualMileageKm,
            keysTransferred, documentsTransferred, equipmentTransferred, conditionConfirmed, issuerConfirmed,
            responsibleConfirmed, Require(conditionNotes, 2000, "Состояние автомобиля обязательно."),
            NormalizeOptional(comments, 2000), actorUserId, now);
        Record(commandId, "HandoverCompleted", signature, actorUserId, now); return true;
    }

    public bool Complete(Guid commandId, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        if (EnsureCommand(commandId, "Completed", null)) return false;
        EnsureVersion(expectedVersion); EnsureStatus(DealStatus.ReadyForHandover);
        if (Handover is null) throw new DomainException("deal.handover_required", "Checklist выдачи не завершён.");
        if (NetPaid != FinalTotalAmount || !HasRequiredDocuments())
            throw new DomainException("deal.completion_blocked", "Оплата или документы сделки неполны.");
        Status = DealStatus.Completed; CompletedAt = now; ClosedAt = now;
        Record(commandId, "Completed", null, actorUserId, now); return true;
    }

    public bool Cancel(Guid commandId, string? reason, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        var normalized = Require(reason, 2000, "Причина отмены сделки обязательна.");
        if (EnsureCommand(commandId, "Cancelled", normalized)) return false;
        EnsureVersion(expectedVersion);
        if (Status is DealStatus.Completed or DealStatus.Cancelled or DealStatus.Refunded)
            throw new DomainException("deal.final", "Финальную сделку нельзя отменить.");
        Status = NetPaid > 0 ? DealStatus.RefundPending : DealStatus.Cancelled;
        ClosureReason = normalized;
        if (Status == DealStatus.Cancelled) ClosedAt = now;
        Record(commandId, "Cancelled", normalized, actorUserId, now); return true;
    }

    public bool HasRequiredDocuments() => Enum.GetValues<DealDocumentType>().All(type =>
        _documents.Any(x => x.Type == type));

    private bool EnsureCommand(Guid commandId, string operation, string? signature)
    {
        if (commandId == Guid.Empty) throw new DomainException("deal.command_id_required", "Command ID обязателен.");
        var existing = _history.SingleOrDefault(x => x.CommandId == commandId);
        if (existing is null) return false;
        if (existing.Operation != operation || existing.Signature != signature)
            throw new ConflictException("deal.command_conflict", "Command ID сделки использован с другим payload.");
        return true;
    }
    private void Record(Guid commandId, string operation, string? signature, Guid actorUserId, DateTimeOffset now)
    {
        _history.Add(new DealHistory(Guid.NewGuid(), OrganizationId, Id, commandId, operation, signature, actorUserId,
        now)); UpdatedAt = now; Version++;
    }
    private void EnsureVersion(long expectedVersion)
    { if (Version != expectedVersion) throw new ConflictException("deal.version_conflict", "Сделка изменена конкурентно."); }
    private void EnsureStatus(DealStatus status)
    { if (Status != status) throw new DomainException("deal.status_conflict", $"Команда недоступна в статусе {Status}."); }
    private static string BuildCreateSignature(Guid reservationId, Guid snapshotId, decimal total, string currency) =>
        JsonSerializer.Serialize(new { reservationId, snapshotId, total, currency });
    private static decimal Positive(decimal value, string? currency, string message)
    { var amount = new Money(value, currency).Amount; if (amount <= 0) throw new DomainException("deal.positive_amount_required", message); return amount; }
    private static decimal NonNegative(decimal value, string? currency) => new Money(value, currency).Amount;
    private static string Require(string? value, int max, string message)
    { if (string.IsNullOrWhiteSpace(value)) throw new DomainException("deal.required", message); var safe = value.Trim(); if (safe.Length > max) throw new DomainException("deal.too_long", $"Максимальная длина — {max} символов."); return safe; }
    private static string? NormalizeOptional(string? value, int max)
    { if (string.IsNullOrWhiteSpace(value)) return null; var safe = value.Trim(); if (safe.Length > max) throw new DomainException("deal.too_long", $"Максимальная длина — {max} символов."); return safe; }
}

public sealed class DealPayment
{
    private DealPayment() { }
    internal DealPayment(Guid id, Guid organizationId, Guid dealId, Guid commandId, PaymentKind kind,
        PaymentStatus status, decimal amount, string currency, string manualReference, string reason,
        Guid actorUserId, DateTimeOffset occurredAt, DateTimeOffset recordedAt)
    {
        Id = id; OrganizationId = organizationId; DealId = dealId; CommandId = commandId; Kind = kind; Status = status;
        Amount = amount; Currency = currency; ManualReference = manualReference; Reason = reason;
        ActorUserId = actorUserId; OccurredAt = occurredAt; RecordedAt = recordedAt;
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid DealId { get; private set; }
    public Guid CommandId { get; private set; }
    public PaymentKind Kind { get; private set; }
    public PaymentStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string ManualReference { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    internal bool Matches(Guid id, Guid commandId, PaymentKind kind, PaymentStatus status, decimal amount,
        string currency, string reference, string reason, Guid actor) => Id == id && CommandId == commandId
        && Kind == kind && Status == status && Amount == amount && Currency == currency
        && ManualReference == reference && Reason == reason && ActorUserId == actor;
}

public sealed class DealDocument
{
    private DealDocument() { }
    internal DealDocument(Guid id, Guid organizationId, Guid dealId, Guid commandId, DealDocumentType type,
        string number, string templateName, int templateVersion, int revision, Guid? sourceDocumentId,
        string sha256, string objectKey, long sizeBytes, string? reason, Guid generatedByUserId,
        DateTimeOffset generatedAt)
    {
        Id = id; OrganizationId = organizationId; DealId = dealId; CommandId = commandId; Type = type;
        Number = number; TemplateName = templateName; TemplateVersion = templateVersion; Revision = revision;
        SourceDocumentId = sourceDocumentId; Sha256 = sha256; ObjectKey = objectKey; SizeBytes = sizeBytes;
        Reason = reason; GeneratedByUserId = generatedByUserId; GeneratedAt = generatedAt;
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid DealId { get; private set; }
    public Guid CommandId { get; private set; }
    public DealDocumentType Type { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public string TemplateName { get; private set; } = string.Empty;
    public int TemplateVersion { get; private set; }
    public int Revision { get; private set; }
    public Guid? SourceDocumentId { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public string ObjectKey { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string? Reason { get; private set; }
    public Guid GeneratedByUserId { get; private set; }
    public DateTimeOffset GeneratedAt { get; private set; }
    internal bool Matches(Guid id, Guid commandId, DealDocumentType type, string sha256, Guid actor) =>
        Id == id && CommandId == commandId && Type == type && Sha256 == sha256 && GeneratedByUserId == actor;
}

public sealed class DealHandoverSnapshot
{
    private DealHandoverSnapshot() { }
    internal DealHandoverSnapshot(Guid id, Guid organizationId, Guid dealId, int actualMileageKm,
        bool keysTransferred, bool documentsTransferred, bool equipmentTransferred, bool conditionConfirmed,
        bool issuerConfirmed, bool responsibleConfirmed, string conditionNotes, string? comments,
        Guid completedByUserId, DateTimeOffset completedAt)
    {
        Id = id; OrganizationId = organizationId; DealId = dealId; ActualMileageKm = actualMileageKm;
        KeysTransferred = keysTransferred; DocumentsTransferred = documentsTransferred;
        EquipmentTransferred = equipmentTransferred; ConditionConfirmed = conditionConfirmed;
        IssuerConfirmed = issuerConfirmed; ResponsibleConfirmed = responsibleConfirmed;
        ConditionNotes = conditionNotes; Comments = comments; CompletedByUserId = completedByUserId;
        CompletedAt = completedAt;
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid DealId { get; private set; }
    public int ActualMileageKm { get; private set; }
    public bool KeysTransferred { get; private set; }
    public bool DocumentsTransferred { get; private set; }
    public bool EquipmentTransferred { get; private set; }
    public bool ConditionConfirmed { get; private set; }
    public bool IssuerConfirmed { get; private set; }
    public bool ResponsibleConfirmed { get; private set; }
    public string ConditionNotes { get; private set; } = string.Empty;
    public string? Comments { get; private set; }
    public Guid CompletedByUserId { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }
}

public sealed class DealHistory
{
    private DealHistory() { }
    internal DealHistory(Guid id, Guid organizationId, Guid dealId, Guid commandId, string operation,
        string? signature, Guid actorUserId, DateTimeOffset occurredAt)
    {
        Id = id; OrganizationId = organizationId; DealId = dealId; CommandId = commandId; Operation = operation;
        Signature = signature; ActorUserId = actorUserId; OccurredAt = occurredAt;
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid DealId { get; private set; }
    public Guid CommandId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string? Signature { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
