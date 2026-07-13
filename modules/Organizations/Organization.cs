namespace DealerOS.Modules.Organizations;

public sealed class Organization
{
    private Organization() { }
    public Organization(Guid id, string name, bool requireIndependentReconditioningApproval = true) =>
        (Id, Name, RequireIndependentReconditioningApproval) =
        (id, name.Trim(), requireIndependentReconditioningApproval);
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool RequireIndependentReconditioningApproval { get; private set; }
}

public sealed class Branch
{
    private Branch() { }
    public Branch(Guid id, Guid organizationId, string code, string name)
    {
        Id = id;
        OrganizationId = organizationId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
}
