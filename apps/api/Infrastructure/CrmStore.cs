using DealerOS.Modules.Crm.Application;
using DealerOS.Modules.Crm.Domain;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DealerOS.Api.Infrastructure;

public sealed class CrmStore(DealerOsDbContext dbContext) : ICrmStore
{
    public Task<bool> BranchExistsAsync(Guid organizationId, Guid branchId, CancellationToken cancellationToken) =>
        dbContext.Branches.AnyAsync(x => x.OrganizationId == organizationId && x.Id == branchId, cancellationToken);
    public Task<int> GetLeadSlaMinutesAsync(Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.Organizations.Where(x => x.Id == organizationId).Select(x => x.LeadFirstResponseSlaMinutes)
            .SingleAsync(cancellationToken);
    public Task<Customer?> FindCustomerAsync(Guid organizationId, Guid customerId,
        CancellationToken cancellationToken) => dbContext.Customers.SingleOrDefaultAsync(
        x => x.OrganizationId == organizationId && x.Id == customerId, cancellationToken);
    public async Task<IReadOnlyList<Customer>> SearchCustomersAsync(Guid organizationId,
        IReadOnlySet<Guid> branchIds, string? query, CancellationToken cancellationToken)
    {
        var customers = dbContext.Customers.AsNoTracking().Where(x => x.OrganizationId == organizationId
            && branchIds.Contains(x.CreatedInBranchId));
        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = query.Trim().ToLowerInvariant();
            string? phone = null;
            try { phone = Customer.NormalizePhone(query); } catch (DomainException) { }
            var email = normalized.Contains('@') ? Customer.NormalizeEmail(normalized) : null;
            customers = customers.Where(x => EF.Functions.ILike(x.Name, $"%{normalized}%")
                || (phone != null && x.NormalizedPhone == phone) || (email != null && x.NormalizedEmail == email));
        }
        return await customers.OrderBy(x => x.MergedIntoCustomerId != null).ThenBy(x => x.Name).Take(100)
            .ToListAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<Customer>> FindDuplicatesAsync(Guid organizationId, string? normalizedPhone,
        string? normalizedEmail, Guid? excludeId, CancellationToken cancellationToken) => await dbContext.Customers
        .AsNoTracking().Where(x => x.OrganizationId == organizationId && x.MergedIntoCustomerId == null
            && x.Id != excludeId && ((normalizedPhone != null && x.NormalizedPhone == normalizedPhone)
                || (normalizedEmail != null && x.NormalizedEmail == normalizedEmail))).Take(20)
        .ToListAsync(cancellationToken);
    public Task AddCustomerAsync(Customer customer, CancellationToken cancellationToken) =>
        dbContext.Customers.AddAsync(customer, cancellationToken).AsTask();
    public Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken) => dbContext.Vehicles.AsNoTracking().SingleOrDefaultAsync(
        x => x.OrganizationId == organizationId && x.Id == vehicleId, cancellationToken);
    public Task<Lead?> FindLeadAsync(Guid organizationId, Guid leadId, CancellationToken cancellationToken) =>
        LeadQuery().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == leadId,
            cancellationToken);
    public async Task<IReadOnlyList<Lead>> ListLeadsAsync(Guid organizationId, IReadOnlySet<Guid> branchIds,
        CancellationToken cancellationToken) => await LeadQuery().Where(x => x.OrganizationId == organizationId
            && branchIds.Contains(x.BranchId)).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<Lead>> ListCustomerLeadsAsync(Guid organizationId, Guid customerId,
        CancellationToken cancellationToken) => await LeadQuery().Where(x => x.OrganizationId == organizationId
            && x.CustomerId == customerId).ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<UserAccount>> ListAssignableManagersAsync(Guid organizationId, Guid branchId,
        CancellationToken cancellationToken) => (await dbContext.Users.AsNoTracking().Include(x => x.BranchAccess)
            .Where(x => x.OrganizationId == organizationId && x.IsActive
                && x.BranchAccess.Any(access => access.BranchId == branchId)).ToListAsync(cancellationToken))
            .Where(x => x.PermissionSet.Contains(Permissions.CrmLeadsWork)).ToArray();
    public Task<UserAccount?> FindUserAsync(Guid organizationId, Guid userId,
        CancellationToken cancellationToken) => dbContext.Users.AsNoTracking().SingleOrDefaultAsync(
        x => x.OrganizationId == organizationId && x.Id == userId, cancellationToken);
    public async Task<IReadOnlyDictionary<Guid, int>> CountActiveLeadsByManagerAsync(Guid organizationId,
        Guid branchId, CancellationToken cancellationToken) => await dbContext.Leads.AsNoTracking()
        .Where(x => x.OrganizationId == organizationId && x.BranchId == branchId
            && x.AssignedManagerUserId != null && (int)x.Status <= (int)LeadStatus.Qualified)
        .GroupBy(x => x.AssignedManagerUserId!.Value).ToDictionaryAsync(x => x.Key, x => x.Count(),
            cancellationToken);
    public Task AddLeadAsync(Lead lead, CancellationToken cancellationToken) =>
        dbContext.Leads.AddAsync(lead, cancellationToken).AsTask();
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        { throw new ConflictException("crm.version_conflict", "CRM запись изменена конкурентно."); }
        catch (DbUpdateException)
        { throw new ConflictException("crm.constraint_conflict", "CRM команда конфликтует с сохранёнными данными."); }
    }
    public void ResetTracking() => dbContext.ChangeTracker.Clear();
    private IQueryable<Lead> LeadQuery() => dbContext.Leads.Include(x => x.Activities).Include(x => x.History)
        .AsSplitQuery();
}
