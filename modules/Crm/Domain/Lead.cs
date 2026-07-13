using System.Text.Json;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Crm.Domain;

public enum LeadStatus { New = 1, Assigned = 2, FirstContact = 3, Qualified = 4, Lost = 5, Spam = 6, Duplicate = 7, Deferred = 8 }
public enum LeadActivityType { Call = 1, Message = 2, Email = 3, Note = 4, Task = 5 }
public enum LeadActivityDirection { Inbound = 1, Outbound = 2, Internal = 3 }

public sealed class Lead
{
    private readonly List<LeadActivity> _activities = [];
    private readonly List<LeadStatusHistory> _history = [];
    private Lead() { }
    private Lead(Guid organizationId, Guid branchId, Guid customerId, Guid? vehicleId, string? searchCriteria,
        string? source, Guid actorUserId, int slaMinutes, DateTimeOffset now)
    {
        if (vehicleId is null && string.IsNullOrWhiteSpace(searchCriteria))
            throw new DomainException("crm.lead_interest_required", "Укажите автомобиль или параметры поиска.");
        if (slaMinutes is < 1 or > 10_080) throw new DomainException("crm.invalid_sla", "SLA должен быть от 1 минуты до 7 дней.");
        Id = Guid.NewGuid(); OrganizationId = organizationId; BranchId = branchId; CustomerId = customerId;
        VehicleId = vehicleId; SearchCriteria = Normalize(searchCriteria, 2000); Source = Require(source, 100,
            "Источник лида обязателен."); CreatedByUserId = actorUserId; Status = LeadStatus.New; CreatedAt = now;
        UpdatedAt = now; FirstResponseDueAt = now.AddMinutes(slaMinutes); Version = 1;
        _history.Add(new LeadStatusHistory(Guid.NewGuid(), organizationId, Id, Guid.NewGuid(), null, Status,
            "Created", null, actorUserId, now));
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? VehicleId { get; private set; }
    public string? SearchCriteria { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public Guid? AssignedManagerUserId { get; private set; }
    public LeadStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }
    public DateTimeOffset FirstResponseDueAt { get; private set; }
    public DateTimeOffset? FirstResponseAt { get; private set; }
    public string? LostReason { get; private set; }
    public string? NextAction { get; private set; }
    public DateTimeOffset? NextActionDueAt { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<LeadActivity> Activities => _activities;
    public IReadOnlyCollection<LeadStatusHistory> History => _history;
    public bool IsSlaBreached(DateTimeOffset now) => FirstResponseAt is null && now > FirstResponseDueAt;
    public Guid? FindAssignedManagerForCommand(Guid commandId)
    {
        var existing = _history.SingleOrDefault(x => x.CommandId == commandId);
        if (existing is null) return null;
        if (existing.Operation != "Assigned" || !Guid.TryParseExact(existing.Signature, "N", out var managerId))
            throw new ConflictException("crm.command_id_conflict", "Command ID уже использован другой командой.");
        return managerId;
    }
    public static Lead Create(Guid organizationId, Guid branchId, Guid customerId, Guid? vehicleId,
        string? searchCriteria, string? source, Guid actorUserId, int slaMinutes, DateTimeOffset now) =>
        new(organizationId, branchId, customerId, vehicleId, searchCriteria, source, actorUserId, slaMinutes, now);

    public bool Assign(Guid commandId, Guid managerUserId, Guid actorUserId, long expectedVersion,
        DateTimeOffset now)
    {
        var signature = managerUserId.ToString("N"); if (EnsureCommand(commandId, "Assigned", signature)) return false;
        EnsureVersion(expectedVersion); EnsureNotFinal();
        AssignedManagerUserId = managerUserId; AssignedAt ??= now;
        Transition(LeadStatus.Assigned, commandId, actorUserId, signature, now); return true;
    }

    public bool AddActivity(Guid commandId, LeadActivityType type, LeadActivityDirection direction, string? result,
        string? summary, bool meaningfulContact, string? nextAction, DateTimeOffset? nextActionDueAt,
        Guid actorUserId, long expectedVersion, DateTimeOffset now)
    {
        var normalizedSummary = Require(summary, 1000, "Краткое summary обязательно.");
        var signature = JsonSerializer.Serialize(new
        {
            type,
            direction,
            result,
            normalizedSummary,
            meaningfulContact,
            nextAction,
            nextActionDueAt
        });
        if (EnsureCommand(commandId, "Activity", signature)) return false;
        EnsureVersion(expectedVersion); EnsureNotFinal();
        if (meaningfulContact && AssignedManagerUserId is null)
            throw new DomainException("crm.assignment_required", "Перед первым контактом назначьте менеджера.");
        _activities.Add(new LeadActivity(Guid.NewGuid(), OrganizationId, Id, commandId, type, direction,
            Normalize(result, 100), normalizedSummary, actorUserId, type != LeadActivityType.Task, now));
        NextAction = Normalize(nextAction, 1000); NextActionDueAt = nextActionDueAt;
        if (meaningfulContact && FirstResponseAt is null)
        {
            FirstResponseAt = now;
            Transition(LeadStatus.FirstContact, Guid.NewGuid(), actorUserId, "First meaningful contact", now);
        }
        _history.Add(new LeadStatusHistory(Guid.NewGuid(), OrganizationId, Id, commandId, Status, Status,
            "Activity", signature, actorUserId, now)); Touch(now); return true;
    }

    public bool CompleteActivity(Guid commandId, Guid activityId, Guid actorUserId, long expectedVersion,
        DateTimeOffset now)
    {
        if (EnsureCommand(commandId, "ActivityCompleted", activityId.ToString("N"))) return false;
        EnsureVersion(expectedVersion);
        var activity = _activities.SingleOrDefault(x => x.Id == activityId)
            ?? throw new DomainException("crm.activity_missing", "Activity не найдена.");
        activity.Complete(now);
        _history.Add(new LeadStatusHistory(Guid.NewGuid(), OrganizationId, Id, commandId, Status, Status,
            "ActivityCompleted", activityId.ToString("N"), actorUserId, now)); Touch(now); return true;
    }

    public bool Qualify(Guid commandId, Guid actorUserId, string? nextAction, DateTimeOffset? dueAt,
        long expectedVersion, DateTimeOffset now)
    {
        var signature = JsonSerializer.Serialize(new { nextAction, dueAt });
        if (EnsureCommand(commandId, "Qualified", signature)) return false;
        EnsureVersion(expectedVersion);
        if (Status != LeadStatus.FirstContact)
            throw new DomainException("crm.first_contact_required", "Квалификация доступна после First Contact.");
        NextAction = Require(nextAction, 1000, "Следующее действие обязательно."); NextActionDueAt = dueAt;
        Transition(LeadStatus.Qualified, commandId, actorUserId, signature, now); return true;
    }

    public bool Close(Guid commandId, LeadStatus finalStatus, Guid actorUserId, string? reason,
        long expectedVersion, DateTimeOffset now)
    {
        if (finalStatus is not (LeadStatus.Lost or LeadStatus.Spam or LeadStatus.Duplicate or LeadStatus.Deferred))
            throw new DomainException("crm.invalid_final_status", "Недопустимый финальный статус.");
        var normalized = Require(reason, 2000, "Причина обязательна.");
        if (EnsureCommand(commandId, finalStatus.ToString(), normalized)) return false;
        EnsureVersion(expectedVersion); EnsureNotFinal();
        if (finalStatus == LeadStatus.Lost) LostReason = normalized;
        Transition(finalStatus, commandId, actorUserId, normalized, now); return true;
    }

    public void ReassignCustomer(Guid targetCustomerId, Guid commandId, Guid actorUserId, DateTimeOffset now)
    {
        if (EnsureCommand(commandId, "CustomerMerged", targetCustomerId.ToString("N"))) return;
        CustomerId = targetCustomerId;
        _history.Add(new LeadStatusHistory(Guid.NewGuid(), OrganizationId, Id, commandId, Status, Status,
            "CustomerMerged", targetCustomerId.ToString("N"), actorUserId, now)); Touch(now);
    }
    private bool EnsureCommand(Guid commandId, string operation, string signature)
    {
        var existing = _history.SingleOrDefault(x => x.CommandId == commandId);
        if (existing is null) return false;
        if (existing.Operation != operation || existing.Signature != signature)
            throw new ConflictException("crm.command_id_conflict", "Command ID уже использован с другим payload.");
        return true;
    }
    private void Transition(LeadStatus status, Guid commandId, Guid actorUserId, string? reason, DateTimeOffset now)
    {
        var from = Status; Status = status;
        _history.Add(new LeadStatusHistory(Guid.NewGuid(), OrganizationId, Id, commandId, from, status,
            status.ToString(), reason, actorUserId, now)); Touch(now);
    }
    private void EnsureVersion(long expectedVersion)
    { if (Version != expectedVersion) throw new ConflictException("crm.lead_version_conflict", "Lead изменён конкурентно."); }
    private void EnsureNotFinal()
    { if (Status is LeadStatus.Lost or LeadStatus.Spam or LeadStatus.Duplicate or LeadStatus.Deferred) throw new DomainException("crm.lead_closed", "Закрытый lead неизменяем."); }
    private void Touch(DateTimeOffset now) { UpdatedAt = now; Version++; }
    private static string Require(string? value, int max, string message)
    { if (string.IsNullOrWhiteSpace(value)) throw new DomainException("crm.required", message); return Normalize(value, max)!; }
    private static string? Normalize(string? value, int max)
    { if (string.IsNullOrWhiteSpace(value)) return null; var result = value.Trim(); if (result.Length > max) throw new DomainException("crm.too_long", $"Максимальная длина — {max} символов."); return result; }
}

public sealed class LeadActivity
{
    private LeadActivity() { }
    internal LeadActivity(Guid id, Guid organizationId, Guid leadId, Guid commandId, LeadActivityType type,
        LeadActivityDirection direction, string? result, string summary, Guid actorUserId, bool isClosed,
        DateTimeOffset now)
    { Id = id; OrganizationId = organizationId; LeadId = leadId; CommandId = commandId; Type = type; Direction = direction; Result = result; Summary = summary; ActorUserId = actorUserId; IsClosed = isClosed; CreatedAt = now; CompletedAt = isClosed ? now : null; }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid CommandId { get; private set; }
    public LeadActivityType Type { get; private set; }
    public LeadActivityDirection Direction { get; private set; }
    public string? Result { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public bool IsClosed { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    internal void Complete(DateTimeOffset now)
    { if (IsClosed) return; IsClosed = true; CompletedAt = now; }
}

public sealed class LeadStatusHistory
{
    private LeadStatusHistory() { }
    internal LeadStatusHistory(Guid id, Guid organizationId, Guid leadId, Guid commandId, LeadStatus? fromStatus,
        LeadStatus toStatus, string operation, string? signature, Guid actorUserId, DateTimeOffset occurredAt)
    { Id = id; OrganizationId = organizationId; LeadId = leadId; CommandId = commandId; FromStatus = fromStatus; ToStatus = toStatus; Operation = operation; Signature = signature; ActorUserId = actorUserId; OccurredAt = occurredAt; }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid CommandId { get; private set; }
    public LeadStatus? FromStatus { get; private set; }
    public LeadStatus ToStatus { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string? Signature { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
