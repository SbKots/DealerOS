namespace DealerOS.Modules.IdentityAccess;

public sealed class UserAccount
{
    private UserAccount() { }

    public UserAccount(Guid id, Guid organizationId, string email, string displayName, string passwordHash, string permissions)
    {
        Id = id;
        OrganizationId = organizationId;
        Email = email.Trim().ToLowerInvariant();
        DisplayName = displayName.Trim();
        PasswordHash = passwordHash;
        Permissions = permissions;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Permissions { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public ICollection<UserBranchAccess> BranchAccess { get; private set; } = new List<UserBranchAccess>();

    public IReadOnlyCollection<string> PermissionSet => Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void GrantBranch(Guid branchId) => BranchAccess.Add(new UserBranchAccess(OrganizationId, Id, branchId));

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void SetPermissions(IEnumerable<string> permissions) => Permissions = string.Join(',', permissions.Distinct(StringComparer.Ordinal));
}

public sealed class UserBranchAccess
{
    private UserBranchAccess() { }
    public UserBranchAccess(Guid organizationId, Guid userId, Guid branchId) =>
        (OrganizationId, UserId, BranchId) = (organizationId, userId, branchId);
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid BranchId { get; private set; }
}
