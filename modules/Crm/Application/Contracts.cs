using DealerOS.Modules.Crm.Domain;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Vehicles.Domain;

namespace DealerOS.Modules.Crm.Application;

public sealed record CreateCustomerRequest(Guid BranchId, CustomerType Type, string? Name, string? Phone,
    string? Email, PreferredContactChannel PreferredChannel, bool ConsentGiven, bool MarketingConsent,
    DateTimeOffset? ConsentAt, string? ConsentSource);
public sealed record CustomerResponse(Guid Id, Guid CreatedInBranchId, string Type, string Name,
    string? NormalizedPhone, string? NormalizedEmail, string PreferredChannel, bool ConsentGiven,
    bool MarketingConsent, DateTimeOffset? ConsentAt, string? ConsentSource, bool IsMerged,
    Guid? MergedIntoCustomerId, long Version, DateTimeOffset CreatedAt);
public sealed record CustomerCreateResponse(CustomerResponse Customer,
    IReadOnlyList<CustomerResponse> PossibleDuplicates);
public sealed record CustomerMergePreviewResponse(CustomerResponse Source, CustomerResponse Target,
    int LeadsToMove, string Warning);
public sealed record MergeCustomersRequest(Guid CommandId, Guid TargetCustomerId, string? Reason,
    long ExpectedSourceVersion);

public sealed record CreateLeadRequest(Guid BranchId, Guid CustomerId, Guid? VehicleId, string? SearchCriteria,
    string? Source);
public sealed record AssignLeadRequest(Guid CommandId, Guid ManagerUserId, long ExpectedVersion);
public sealed record RoundRobinAssignLeadRequest(Guid CommandId, long ExpectedVersion);
public sealed record AddLeadActivityRequest(Guid CommandId, LeadActivityType Type,
    LeadActivityDirection Direction, string? Result, string? Summary, bool MeaningfulContact,
    string? NextAction, DateTimeOffset? NextActionDueAt, long ExpectedVersion);
public sealed record CompleteLeadActivityRequest(Guid CommandId, long ExpectedVersion);
public sealed record QualifyLeadRequest(Guid CommandId, string? NextAction, DateTimeOffset? NextActionDueAt,
    long ExpectedVersion);
public sealed record CloseLeadRequest(Guid CommandId, LeadStatus Status, string? Reason, long ExpectedVersion);
public sealed record AssignableManagerResponse(Guid Id, string DisplayName);
public sealed record LeadActivityResponse(Guid Id, string Type, string Direction, string? Result, string Summary,
    Guid ActorUserId, bool IsClosed, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
public sealed record LeadHistoryResponse(Guid CommandId, string? FromStatus, string ToStatus, string Operation,
    DateTimeOffset OccurredAt);
public sealed record LeadResponse(Guid Id, Guid BranchId, Guid CustomerId, string CustomerName, Guid? VehicleId,
    string? SearchCriteria, string Source, Guid? AssignedManagerUserId, string? AssignedManagerName,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? AssignedAt, DateTimeOffset FirstResponseDueAt,
    DateTimeOffset? FirstResponseAt, bool SlaBreached, string? LostReason, string? NextAction,
    DateTimeOffset? NextActionDueAt, long Version, IReadOnlyList<LeadActivityResponse> Activities,
    IReadOnlyList<LeadHistoryResponse> History);

public interface ICrmStore
{
    Task<bool> BranchExistsAsync(Guid organizationId, Guid branchId, CancellationToken cancellationToken);
    Task<int> GetLeadSlaMinutesAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<Customer?> FindCustomerAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Customer>> SearchCustomersAsync(Guid organizationId, IReadOnlySet<Guid> branchIds,
        string? query,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Customer>> FindDuplicatesAsync(Guid organizationId, string? normalizedPhone,
        string? normalizedEmail, Guid? excludeId, CancellationToken cancellationToken);
    Task AddCustomerAsync(Customer customer, CancellationToken cancellationToken);
    Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<Lead?> FindLeadAsync(Guid organizationId, Guid leadId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Lead>> ListLeadsAsync(Guid organizationId, IReadOnlySet<Guid> branchIds,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Lead>> ListCustomerLeadsAsync(Guid organizationId, Guid customerId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<UserAccount>> ListAssignableManagersAsync(Guid organizationId, Guid branchId,
        CancellationToken cancellationToken);
    Task<UserAccount?> FindUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, int>> CountActiveLeadsByManagerAsync(Guid organizationId, Guid branchId,
        CancellationToken cancellationToken);
    Task AddLeadAsync(Lead lead, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    void ResetTracking();
}
