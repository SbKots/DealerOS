namespace DealerOS.Api.Infrastructure;

public sealed class AuditEvent
{
    private AuditEvent() { }
    public AuditEvent(Guid id, Guid organizationId, Guid actorUserId, string operation, string entityType,
        Guid entityId, string? oldValue, string newValue, string correlationId, DateTimeOffset occurredAt)
    {
        Id = id;
        OrganizationId = organizationId;
        ActorUserId = actorUserId;
        Operation = operation;
        EntityType = entityType;
        EntityId = entityId;
        OldValue = oldValue;
        NewValue = newValue;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public string? OldValue { get; private set; }
    public string NewValue { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
}
