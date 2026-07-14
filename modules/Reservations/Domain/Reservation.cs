using System.Text.Json;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Reservations.Domain;

public enum ReservationStatus
{
    PendingDeposit = 1,
    Active = 2,
    ConvertedToDeal = 3,
    Expired = 4,
    Cancelled = 5,
    DepositFailed = 6
}

public enum ReservationDepositStatus
{
    NotRequired = 1,
    Pending = 2,
    Received = 3,
    Failed = 4,
    Refunded = 5,
    Forfeited = 6
}

public sealed class Reservation
{
    private readonly List<ReservationHistory> _history = [];
    private Reservation() { }

    private Reservation(Guid id, Guid organizationId, Guid branchId, Guid vehicleId, Guid customerId, Guid leadId,
        Guid approvedOfferSnapshotId, Guid createdByUserId, Guid createCommandId, DateTimeOffset expiresAt,
        bool depositRequired, decimal depositAmount, string currency, DateTimeOffset now)
    {
        if (id == Guid.Empty || createCommandId == Guid.Empty || approvedOfferSnapshotId == Guid.Empty)
            throw new DomainException("reservation.identifier_required", "Идентификаторы брони и команды обязательны.");
        if (expiresAt <= now || expiresAt > now.AddDays(30))
            throw new DomainException("reservation.invalid_expiration", "Срок брони должен быть в пределах 30 дней.");

        var money = new Money(depositAmount, currency);
        if (depositRequired && money.Amount <= 0)
            throw new DomainException("reservation.deposit_required", "Требуемая предоплата должна быть положительной.");
        if (!depositRequired && money.Amount != 0)
            throw new DomainException("reservation.deposit_not_required", "Для брони без предоплаты сумма должна быть нулевой.");

        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        VehicleId = vehicleId;
        CustomerId = customerId;
        LeadId = leadId;
        ApprovedOfferSnapshotId = approvedOfferSnapshotId;
        CreatedByUserId = createdByUserId;
        CreateCommandId = createCommandId;
        CreatedAt = now;
        StartsAt = now;
        ExpiresAt = expiresAt;
        DepositAmount = money.Amount;
        Currency = money.Currency;
        DepositStatus = depositRequired ? ReservationDepositStatus.Pending : ReservationDepositStatus.NotRequired;
        Status = depositRequired ? ReservationStatus.PendingDeposit : ReservationStatus.Active;
        Version = 1;
        CreateSignature = BuildCreateSignature(approvedOfferSnapshotId, expiresAt, depositRequired,
            DepositAmount, Currency);
        _history.Add(new ReservationHistory(Guid.NewGuid(), organizationId, id, createCommandId, "Created",
            CreateSignature, createdByUserId, now));
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid ApprovedOfferSnapshotId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid CreateCommandId { get; private set; }
    public string CreateSignature { get; private set; } = string.Empty;
    public ReservationStatus Status { get; private set; }
    public ReservationDepositStatus DepositStatus { get; private set; }
    public decimal DepositAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string? DepositReference { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? ClosureReason { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<ReservationHistory> History => _history;

    public static Reservation Create(Guid id, Guid organizationId, Guid branchId, Guid vehicleId, Guid customerId,
        Guid leadId, Guid approvedOfferSnapshotId, Guid createdByUserId, Guid createCommandId,
        DateTimeOffset expiresAt, bool depositRequired, decimal depositAmount, string currency,
        DateTimeOffset now) => new(id, organizationId, branchId, vehicleId, customerId, leadId,
        approvedOfferSnapshotId, createdByUserId, createCommandId, expiresAt, depositRequired, depositAmount,
        currency, now);

    public bool MatchesCreate(Guid approvedOfferSnapshotId, DateTimeOffset expiresAt, bool depositRequired,
        decimal depositAmount, string currency) => CreateSignature == BuildCreateSignature(approvedOfferSnapshotId,
        expiresAt, depositRequired, new Money(depositAmount, currency).Amount, new Money(0, currency).Currency);

    public bool RegisterDeposit(Guid commandId, ReservationDepositStatus result, string? reference, string? reason,
        long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        if (result is not (ReservationDepositStatus.Received or ReservationDepositStatus.Failed))
            throw new DomainException("reservation.invalid_deposit_result", "Допустимы только Received или Failed.");
        var normalizedReference = NormalizeOptional(reference, 200);
        var normalizedReason = result == ReservationDepositStatus.Failed
            ? Require(reason, 2000, "Причина неуспешной предоплаты обязательна.")
            : NormalizeOptional(reason, 2000);
        var signature = JsonSerializer.Serialize(new { result, normalizedReference, normalizedReason });
        if (EnsureCommand(commandId, "DepositRegistered", signature)) return false;
        EnsureVersion(expectedVersion);
        EnsureStatus(ReservationStatus.PendingDeposit);
        EnsureNotExpired(now);
        DepositStatus = result;
        DepositReference = normalizedReference;
        if (result == ReservationDepositStatus.Received)
        {
            Status = ReservationStatus.Active;
        }
        else
        {
            Status = ReservationStatus.DepositFailed;
            ClosedAt = now;
            ClosureReason = normalizedReason;
        }
        Record(commandId, "DepositRegistered", signature, actorUserId, now);
        return true;
    }

    public bool Extend(Guid commandId, DateTimeOffset expiresAt, string? reason, long expectedVersion,
        Guid actorUserId, DateTimeOffset now)
    {
        var normalizedReason = Require(reason, 2000, "Причина продления обязательна.");
        var signature = JsonSerializer.Serialize(new { expiresAt, normalizedReason });
        if (EnsureCommand(commandId, "Extended", signature)) return false;
        EnsureNotExpired(now);
        if (expiresAt <= ExpiresAt || expiresAt <= now || expiresAt > now.AddDays(30))
            throw new DomainException("reservation.invalid_extension", "Новый срок должен увеличить бронь и быть в пределах 30 дней.");
        EnsureVersion(expectedVersion);
        EnsureActive();
        ExpiresAt = expiresAt;
        Record(commandId, "Extended", signature, actorUserId, now);
        return true;
    }

    public bool Cancel(Guid commandId, string? reason, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        var normalized = Require(reason, 2000, "Причина отмены обязательна.");
        if (EnsureCommand(commandId, "Cancelled", normalized)) return false;
        EnsureVersion(expectedVersion);
        EnsureActive();
        Status = ReservationStatus.Cancelled;
        ClosedAt = now;
        ClosureReason = normalized;
        Record(commandId, "Cancelled", normalized, actorUserId, now);
        return true;
    }

    public bool Expire(Guid commandId, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        if (EnsureCommand(commandId, "Expired", null)) return false;
        EnsureVersion(expectedVersion);
        EnsureActive();
        if (now < ExpiresAt)
            throw new DomainException("reservation.not_expired", "Срок брони ещё не истёк.");
        Status = ReservationStatus.Expired;
        ClosedAt = now;
        ClosureReason = "Срок брони истёк";
        Record(commandId, "Expired", null, actorUserId, now);
        return true;
    }

    public bool ConvertToDeal(Guid commandId, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        if (EnsureCommand(commandId, "ConvertedToDeal", null)) return false;
        EnsureVersion(expectedVersion);
        EnsureStatus(ReservationStatus.Active);
        EnsureNotExpired(now);
        Status = ReservationStatus.ConvertedToDeal;
        ClosedAt = now;
        Record(commandId, "ConvertedToDeal", null, actorUserId, now);
        return true;
    }

    private bool EnsureCommand(Guid commandId, string operation, string? signature)
    {
        if (commandId == Guid.Empty)
            throw new DomainException("reservation.command_id_required", "Command ID обязателен.");
        var existing = _history.SingleOrDefault(x => x.CommandId == commandId);
        if (existing is null) return false;
        if (existing.Operation != operation || existing.Signature != signature)
            throw new ConflictException("reservation.command_conflict", "Command ID использован с другим payload.");
        return true;
    }

    private void Record(Guid commandId, string operation, string? signature, Guid actorUserId, DateTimeOffset now)
    {
        _history.Add(new ReservationHistory(Guid.NewGuid(), OrganizationId, Id, commandId, operation, signature,
            actorUserId, now));
        Version++;
    }

    private void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion)
            throw new ConflictException("reservation.version_conflict", "Бронь изменена конкурентно.");
    }

    private void EnsureActive()
    {
        if (Status is not (ReservationStatus.PendingDeposit or ReservationStatus.Active))
            throw new DomainException("reservation.final", "Финальную бронь нельзя изменить.");
    }

    private void EnsureNotExpired(DateTimeOffset now)
    {
        if (now >= ExpiresAt)
            throw new ConflictException("reservation.expired", "Срок брони истёк; дождитесь освобождения автомобиля.");
    }

    private void EnsureStatus(ReservationStatus expected)
    {
        if (Status != expected)
            throw new DomainException("reservation.status_conflict", $"Команда недоступна в статусе {Status}.");
    }

    private static string BuildCreateSignature(Guid snapshotId, DateTimeOffset expiresAt, bool depositRequired,
        decimal depositAmount, string currency) => JsonSerializer.Serialize(new
        {
            snapshotId,
            expiresAt,
            depositRequired,
            depositAmount,
            currency
        });

    private static string Require(string? value, int max, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("reservation.required", message);
        var normalized = value.Trim();
        if (normalized.Length > max) throw new DomainException("reservation.too_long", $"Максимальная длина — {max} символов.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > max) throw new DomainException("reservation.too_long", $"Максимальная длина — {max} символов.");
        return normalized;
    }
}

public sealed class ReservationHistory
{
    private ReservationHistory() { }
    internal ReservationHistory(Guid id, Guid organizationId, Guid reservationId, Guid commandId,
        string operation, string? signature, Guid actorUserId, DateTimeOffset occurredAt)
    {
        Id = id;
        OrganizationId = organizationId;
        ReservationId = reservationId;
        CommandId = commandId;
        Operation = operation;
        Signature = signature;
        ActorUserId = actorUserId;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ReservationId { get; private set; }
    public Guid CommandId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string? Signature { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
