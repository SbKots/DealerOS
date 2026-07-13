namespace DealerOS.Modules.Organizations;

public sealed class Organization
{
    private Organization() { }
    public Organization(Guid id, string name, bool requireIndependentReconditioningApproval = true,
        int leadFirstResponseSlaMinutes = 30, decimal salesAutoApprovalDiscountLimit = 50_000m,
        decimal salesMinimumMarginAmount = 100_000m) =>
        (Id, Name, RequireIndependentReconditioningApproval, LeadFirstResponseSlaMinutes,
            SalesAutoApprovalDiscountLimit, SalesMinimumMarginAmount) =
        (id, name.Trim(), requireIndependentReconditioningApproval,
            leadFirstResponseSlaMinutes is >= 1 and <= 10_080 ? leadFirstResponseSlaMinutes
                : throw new ArgumentOutOfRangeException(nameof(leadFirstResponseSlaMinutes)),
            salesAutoApprovalDiscountLimit is >= 0 and <= 10_000_000m ? salesAutoApprovalDiscountLimit
                : throw new ArgumentOutOfRangeException(nameof(salesAutoApprovalDiscountLimit)),
            salesMinimumMarginAmount is >= 0 and <= 100_000_000m ? salesMinimumMarginAmount
                : throw new ArgumentOutOfRangeException(nameof(salesMinimumMarginAmount)));
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool RequireIndependentReconditioningApproval { get; private set; }
    public int LeadFirstResponseSlaMinutes { get; private set; }
    public decimal SalesAutoApprovalDiscountLimit { get; private set; }
    public decimal SalesMinimumMarginAmount { get; private set; }
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
