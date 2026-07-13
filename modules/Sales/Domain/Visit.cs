using System.Text.Json;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Sales.Domain;

public enum VisitStatus { Scheduled = 1, Arrived = 2, Completed = 3, NoShow = 4, Cancelled = 5 }

public sealed class Visit
{
    private readonly List<VisitHistory> _history = [];
    private Visit() { }
    private Visit(Guid id, Guid organizationId, Guid branchId, Guid customerId, Guid leadId, Guid vehicleId,
        Guid responsibleUserId, DateTimeOffset startsAt, DateTimeOffset endsAt, bool includesTestDrive,
        Guid actorUserId, DateTimeOffset now)
    {
        if (endsAt <= startsAt || endsAt - startsAt > TimeSpan.FromHours(12))
            throw new DomainException("sales.invalid_visit_slot", "Окончание визита должно быть позже начала, длительность — не более 12 часов.");
        if (startsAt < now)
            throw new DomainException("sales.visit_in_past", "Нельзя назначить визит в прошлом.");
        Id = id; OrganizationId = organizationId; BranchId = branchId; CustomerId = customerId; LeadId = leadId;
        VehicleId = vehicleId; ResponsibleUserId = responsibleUserId; StartsAt = startsAt; EndsAt = endsAt;
        IncludesTestDrive = includesTestDrive; CreatedByUserId = actorUserId; Status = VisitStatus.Scheduled;
        CreateSignature = BuildCreateSignature(branchId, customerId, leadId, vehicleId, responsibleUserId,
            startsAt, endsAt, includesTestDrive);
        CreatedAt = now; UpdatedAt = now; Version = 1;
        _history.Add(new VisitHistory(Guid.NewGuid(), organizationId, id, Guid.NewGuid(), "Created", null,
            actorUserId, now));
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid ResponsibleUserId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string CreateSignature { get; private set; } = string.Empty;
    public VisitStatus Status { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public bool IncludesTestDrive { get; private set; }
    public bool DriverDocumentsChecked { get; private set; }
    public string? IssueChecklist { get; private set; }
    public string? ReturnChecklist { get; private set; }
    public int? OdometerOutKm { get; private set; }
    public int? OdometerInKm { get; private set; }
    public string? ConditionOut { get; private set; }
    public string? ConditionIn { get; private set; }
    public DateTimeOffset? CheckedOutAt { get; private set; }
    public DateTimeOffset? CheckedInAt { get; private set; }
    public bool IncidentOccurred { get; private set; }
    public string? IncidentComment { get; private set; }
    public string? Result { get; private set; }
    public string? NextAction { get; private set; }
    public DateTimeOffset? NextActionDueAt { get; private set; }
    public string? ClosureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<VisitHistory> History => _history;

    public static Visit Create(Guid id, Guid organizationId, Guid branchId, Guid customerId, Guid leadId,
        Guid vehicleId, Guid responsibleUserId, DateTimeOffset startsAt, DateTimeOffset endsAt,
        bool includesTestDrive, Guid actorUserId, DateTimeOffset now) => new(id, organizationId, branchId,
            customerId, leadId, vehicleId, responsibleUserId, startsAt, endsAt, includesTestDrive, actorUserId, now);

    public bool MatchesCreate(Guid branchId, Guid customerId, Guid leadId, Guid vehicleId, Guid responsibleUserId,
        DateTimeOffset startsAt, DateTimeOffset endsAt, bool includesTestDrive) => BranchId == branchId
        && CustomerId == customerId && LeadId == leadId && VehicleId == vehicleId
        && CreateSignature == BuildCreateSignature(branchId, customerId, leadId, vehicleId, responsibleUserId,
            startsAt, endsAt, includesTestDrive);

    public bool Reschedule(Guid commandId, DateTimeOffset startsAt, DateTimeOffset endsAt, long expectedVersion,
        Guid actorUserId, DateTimeOffset now)
    {
        var signature = JsonSerializer.Serialize(new { startsAt, endsAt });
        if (EnsureCommand(commandId, "Rescheduled", signature)) return false;
        EnsureVersion(expectedVersion); EnsureStatus(VisitStatus.Scheduled);
        if (startsAt < now || endsAt <= startsAt || endsAt - startsAt > TimeSpan.FromHours(12))
            throw new DomainException("sales.invalid_visit_slot", "Новый слот визита недопустим.");
        StartsAt = startsAt; EndsAt = endsAt; Record(commandId, "Rescheduled", signature, actorUserId, now);
        return true;
    }

    public bool Arrive(Guid commandId, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        if (EnsureCommand(commandId, "Arrived", null)) return false;
        EnsureVersion(expectedVersion); EnsureStatus(VisitStatus.Scheduled); Status = VisitStatus.Arrived;
        Record(commandId, "Arrived", null, actorUserId, now); return true;
    }

    public bool CheckOut(Guid commandId, bool driverDocumentsChecked, string? issueChecklist, int odometerOutKm,
        string? conditionOut, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        var checklist = Require(issueChecklist, 2000, "Checklist выдачи обязателен.");
        var condition = Require(conditionOut, 2000, "Состояние до test drive обязательно.");
        var signature = JsonSerializer.Serialize(new { driverDocumentsChecked, checklist, odometerOutKm, condition });
        if (EnsureCommand(commandId, "TestDriveCheckedOut", signature)) return false;
        EnsureVersion(expectedVersion); EnsureStatus(VisitStatus.Arrived);
        if (!IncludesTestDrive) throw new DomainException("sales.test_drive_not_planned", "Визит не включает test drive.");
        if (CheckedOutAt is not null) throw new DomainException("sales.test_drive_already_out", "Автомобиль уже выдан.");
        if (!driverDocumentsChecked) throw new DomainException("sales.driver_documents_required", "Подтвердите проверку водительских документов.");
        if (odometerOutKm < 0) throw new DomainException("sales.invalid_odometer", "Пробег не может быть отрицательным.");
        DriverDocumentsChecked = true; IssueChecklist = checklist; OdometerOutKm = odometerOutKm;
        ConditionOut = condition; CheckedOutAt = now; Record(commandId, "TestDriveCheckedOut", signature,
            actorUserId, now); return true;
    }

    public bool CheckIn(Guid commandId, string? returnChecklist, int odometerInKm, string? conditionIn,
        bool incidentOccurred, string? incidentComment, long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        var checklist = Require(returnChecklist, 2000, "Checklist возврата обязателен.");
        var condition = Require(conditionIn, 2000, "Состояние после test drive обязательно.");
        var incident = Normalize(incidentComment, 2000);
        var signature = JsonSerializer.Serialize(new { checklist, odometerInKm, condition, incidentOccurred, incident });
        if (EnsureCommand(commandId, "TestDriveCheckedIn", signature)) return false;
        EnsureVersion(expectedVersion); EnsureStatus(VisitStatus.Arrived);
        if (CheckedOutAt is null || CheckedInAt is not null)
            throw new DomainException("sales.test_drive_not_out", "Нет активной выдачи test drive.");
        if (odometerInKm < OdometerOutKm!.Value)
            throw new DomainException("sales.odometer_decreased", "Пробег после test drive не может уменьшиться.");
        if (incidentOccurred && incident is null)
            throw new DomainException("sales.incident_comment_required", "Для инцидента обязателен комментарий.");
        ReturnChecklist = checklist; OdometerInKm = odometerInKm; ConditionIn = condition;
        IncidentOccurred = incidentOccurred; IncidentComment = incident; CheckedInAt = now;
        Record(commandId, "TestDriveCheckedIn", signature, actorUserId, now); return true;
    }

    public bool Complete(Guid commandId, string? result, string? nextAction, DateTimeOffset? nextActionDueAt,
        long expectedVersion, Guid actorUserId, DateTimeOffset now)
    {
        var normalizedResult = Require(result, 2000, "Результат визита обязателен.");
        var normalizedNext = Require(nextAction, 1000, "Следующее действие обязательно.");
        var signature = JsonSerializer.Serialize(new { normalizedResult, normalizedNext, nextActionDueAt });
        if (EnsureCommand(commandId, "Completed", signature)) return false;
        EnsureVersion(expectedVersion); EnsureStatus(VisitStatus.Arrived);
        if (IncludesTestDrive && CheckedInAt is null)
            throw new DomainException("sales.test_drive_return_required", "Сначала завершите возврат test drive.");
        Status = VisitStatus.Completed; Result = normalizedResult; NextAction = normalizedNext;
        NextActionDueAt = nextActionDueAt; Record(commandId, "Completed", signature, actorUserId, now); return true;
    }

    public bool NoShow(Guid commandId, string? reason, long expectedVersion, Guid actorUserId, DateTimeOffset now) =>
        Close(commandId, VisitStatus.NoShow, "NoShow", reason, expectedVersion, actorUserId, now);
    public bool Cancel(Guid commandId, string? reason, long expectedVersion, Guid actorUserId, DateTimeOffset now) =>
        Close(commandId, VisitStatus.Cancelled, "Cancelled", reason, expectedVersion, actorUserId, now);

    private bool Close(Guid commandId, VisitStatus status, string operation, string? reason, long expectedVersion,
        Guid actorUserId, DateTimeOffset now)
    {
        var normalized = Require(reason, 2000, "Причина обязательна.");
        if (EnsureCommand(commandId, operation, normalized)) return false;
        EnsureVersion(expectedVersion); EnsureStatus(VisitStatus.Scheduled);
        Status = status; ClosureReason = normalized; Record(commandId, operation, normalized, actorUserId, now);
        return true;
    }

    private bool EnsureCommand(Guid commandId, string operation, string? signature)
    {
        var existing = _history.SingleOrDefault(x => x.CommandId == commandId);
        if (existing is null) return false;
        if (existing.Operation != operation || existing.Signature != signature)
            throw new ConflictException("sales.visit_command_conflict", "Command ID визита использован с другим payload.");
        return true;
    }
    private void Record(Guid commandId, string operation, string? signature, Guid actorUserId, DateTimeOffset now)
    { _history.Add(new VisitHistory(Guid.NewGuid(), OrganizationId, Id, commandId, operation, signature, actorUserId, now)); UpdatedAt = now; Version++; }
    private void EnsureVersion(long expectedVersion)
    { if (Version != expectedVersion) throw new ConflictException("sales.visit_version_conflict", "Визит изменён конкурентно."); }
    private void EnsureStatus(VisitStatus status)
    { if (Status != status) throw new DomainException("sales.visit_status_conflict", $"Команда недоступна в статусе {Status}."); }
    private static string Require(string? value, int max, string message) => Normalize(value, max)
        ?? throw new DomainException("sales.required", message);
    private static string? Normalize(string? value, int max)
    { if (string.IsNullOrWhiteSpace(value)) return null; var normalized = value.Trim(); if (normalized.Length > max) throw new DomainException("sales.too_long", $"Максимальная длина — {max} символов."); return normalized; }
    private static string BuildCreateSignature(Guid branchId, Guid customerId, Guid leadId, Guid vehicleId,
        Guid responsibleUserId, DateTimeOffset startsAt, DateTimeOffset endsAt, bool includesTestDrive) =>
        JsonSerializer.Serialize(new
        {
            branchId,
            customerId,
            leadId,
            vehicleId,
            responsibleUserId,
            startsAt,
            endsAt,
            includesTestDrive
        });
}

public sealed class VisitHistory
{
    private VisitHistory() { }
    internal VisitHistory(Guid id, Guid organizationId, Guid visitId, Guid commandId, string operation,
        string? signature, Guid actorUserId, DateTimeOffset occurredAt)
    { Id = id; OrganizationId = organizationId; VisitId = visitId; CommandId = commandId; Operation = operation; Signature = signature; ActorUserId = actorUserId; OccurredAt = occurredAt; }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid VisitId { get; private set; }
    public Guid CommandId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string? Signature { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
