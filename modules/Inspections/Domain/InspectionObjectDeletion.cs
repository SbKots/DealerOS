namespace DealerOS.Modules.Inspections.Domain;

public sealed class InspectionObjectDeletion
{
    private InspectionObjectDeletion() { }

    public InspectionObjectDeletion(Guid organizationId, string objectKey, DateTimeOffset createdAt)
    {
        OrganizationId = organizationId;
        ObjectKey = objectKey;
        CreatedAt = createdAt;
    }

    public Guid OrganizationId { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
