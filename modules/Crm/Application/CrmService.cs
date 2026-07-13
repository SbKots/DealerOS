using System.Text.Json;
using DealerOS.Modules.Crm.Domain;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Vehicles.Application;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Crm.Application;

public sealed class CrmService(ICrmStore store, IAuditWriter audit, TimeProvider timeProvider)
{
    public async Task<CustomerCreateResponse> CreateCustomerAsync(ActorContext actor, CreateCustomerRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmCustomersEdit); DemandBranch(actor, request.BranchId);
        if (!await store.BranchExistsAsync(actor.OrganizationId, request.BranchId, cancellationToken))
            throw new ForbiddenException("Филиал не принадлежит организации.");
        var now = timeProvider.GetUtcNow();
        var customer = Customer.Create(actor.OrganizationId, request.BranchId, request.Type, request.Name,
            request.Phone, request.Email, request.PreferredChannel, request.ConsentGiven, request.MarketingConsent,
            request.ConsentAt, request.ConsentSource, actor.UserId, now);
        var duplicates = await store.FindDuplicatesAsync(actor.OrganizationId, customer.NormalizedPhone,
            customer.NormalizedEmail, null, cancellationToken);
        await store.AddCustomerAsync(customer, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "crm.customer_created", "Customer", customer.Id, null,
            JsonSerializer.Serialize(new { customer.Type, customer.PreferredChannel, duplicateCount = duplicates.Count }),
            correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return new CustomerCreateResponse(Map(customer), duplicates.Select(Map).ToArray());
    }

    public async Task<IReadOnlyList<CustomerResponse>> SearchCustomersAsync(ActorContext actor, string? query,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmCustomersView);
        return (await store.SearchCustomersAsync(actor.OrganizationId, actor.BranchIds, query, cancellationToken))
            .Select(Map).ToArray();
    }

    public async Task<CustomerMergePreviewResponse> PreviewMergeAsync(ActorContext actor, Guid sourceCustomerId,
        Guid targetCustomerId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmCustomersMerge);
        var source = await FindCustomerAsync(actor, sourceCustomerId, cancellationToken);
        var target = await FindCustomerAsync(actor, targetCustomerId, cancellationToken);
        if (source.IsMerged || target.IsMerged) throw new DomainException("crm.merge_active_required", "Объединять можно только активных Customers.");
        var leads = await store.ListCustomerLeadsAsync(actor.OrganizationId, source.Id, cancellationToken);
        return new CustomerMergePreviewResponse(Map(source), Map(target), leads.Count,
            "Источник будет сохранён как merged; лиды будут переназначены. Операция аудируется.");
    }

    public async Task<CustomerResponse> MergeAsync(ActorContext actor, Guid sourceCustomerId,
        MergeCustomersRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmCustomersMerge);
        var source = await FindCustomerAsync(actor, sourceCustomerId, cancellationToken);
        var target = await FindCustomerAsync(actor, request.TargetCustomerId, cancellationToken);
        if (target.IsMerged) throw new DomainException("crm.merge_target_inactive", "Target Customer уже объединён.");
        var now = timeProvider.GetUtcNow();
        var changed = source.MergeInto(request.CommandId, target.Id, actor.UserId, request.Reason,
            request.ExpectedSourceVersion, now);
        if (!changed) return Map(source);
        foreach (var lead in await store.ListCustomerLeadsAsync(actor.OrganizationId, source.Id, cancellationToken))
            lead.ReassignCustomer(target.Id, request.CommandId, actor.UserId, now);
        audit.Write(actor.OrganizationId, actor.UserId, "crm.customers_merged", "Customer", source.Id, null,
            JsonSerializer.Serialize(new { targetCustomerId = target.Id }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return Map(source);
    }

    public async Task<LeadResponse> CreateLeadAsync(ActorContext actor, CreateLeadRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmLeadsCreate); DemandBranch(actor, request.BranchId);
        var customer = await FindCustomerAsync(actor, request.CustomerId, cancellationToken);
        if (customer.CreatedInBranchId != request.BranchId)
            throw new ForbiddenException("Customer другого филиала недоступен для этого lead.");
        if (customer.IsMerged) throw new DomainException("crm.customer_merged", "Создайте lead для активного Customer.");
        if (request.VehicleId is { } vehicleId)
        {
            var vehicle = await store.FindVehicleAsync(actor.OrganizationId, vehicleId, cancellationToken)
                ?? throw new NotFoundException("Автомобиль не найден.");
            if (vehicle.BranchId != request.BranchId || vehicle.Status != VehicleStatus.ReadyForSale)
                throw new DomainException("crm.vehicle_not_available", "Lead можно связать с ReadyForSale автомобилем того же филиала.");
        }
        var now = timeProvider.GetUtcNow();
        var lead = Lead.Create(actor.OrganizationId, request.BranchId, customer.Id, request.VehicleId,
            request.SearchCriteria, request.Source, actor.UserId,
            await store.GetLeadSlaMinutesAsync(actor.OrganizationId, cancellationToken), now);
        await store.AddLeadAsync(lead, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "crm.lead_created", "Lead", lead.Id, null,
            JsonSerializer.Serialize(new { lead.BranchId, lead.CustomerId, lead.VehicleId, lead.Source }),
            correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapAsync(actor, lead, cancellationToken);
    }

    public async Task<IReadOnlyList<LeadResponse>> ListLeadsAsync(ActorContext actor,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmLeadsView);
        var leads = await store.ListLeadsAsync(actor.OrganizationId, actor.BranchIds, cancellationToken);
        var result = new List<LeadResponse>();
        foreach (var lead in leads) result.Add(await MapAsync(actor, lead, cancellationToken));
        return result;
    }

    public async Task<IReadOnlyList<AssignableManagerResponse>> ListAssignableManagersAsync(ActorContext actor,
        Guid branchId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmLeadsAssign); DemandBranch(actor, branchId);
        return (await store.ListAssignableManagersAsync(actor.OrganizationId, branchId, cancellationToken))
            .OrderBy(x => x.DisplayName).Select(x => new AssignableManagerResponse(x.Id, x.DisplayName)).ToArray();
    }

    public async Task<LeadResponse> GetLeadAsync(ActorContext actor, Guid leadId,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmLeadsView);
        return await MapAsync(actor, await FindLeadAsync(actor, leadId, cancellationToken), cancellationToken);
    }

    public Task<LeadResponse> AssignAsync(ActorContext actor, Guid leadId, AssignLeadRequest request,
        string correlationId, CancellationToken cancellationToken) => AssignCoreAsync(actor, leadId,
        request.CommandId, request.ExpectedVersion, request.ManagerUserId, correlationId, cancellationToken);

    public async Task<LeadResponse> RoundRobinAssignAsync(ActorContext actor, Guid leadId,
        RoundRobinAssignLeadRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmLeadsAssign);
        var lead = await FindLeadAsync(actor, leadId, cancellationToken);
        if (lead.FindAssignedManagerForCommand(request.CommandId) is not null)
            return await MapAsync(actor, lead, cancellationToken);
        var managers = await store.ListAssignableManagersAsync(actor.OrganizationId, lead.BranchId, cancellationToken);
        if (managers.Count == 0) throw new DomainException("crm.no_assignable_manager", "В филиале нет доступного менеджера.");
        var counts = await store.CountActiveLeadsByManagerAsync(actor.OrganizationId, lead.BranchId,
            cancellationToken);
        var manager = managers.OrderBy(x => counts.GetValueOrDefault(x.Id)).ThenBy(x => x.Id).First();
        return await AssignCoreAsync(actor, leadId, request.CommandId, request.ExpectedVersion, manager.Id,
            correlationId, cancellationToken);
    }

    public async Task<LeadResponse> AddActivityAsync(ActorContext actor, Guid leadId,
        AddLeadActivityRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmLeadsWork);
        var lead = await FindLeadAsync(actor, leadId, cancellationToken); var now = timeProvider.GetUtcNow();
        if (!lead.AddActivity(request.CommandId, request.Type, request.Direction, request.Result, request.Summary,
                request.MeaningfulContact, request.NextAction, request.NextActionDueAt, actor.UserId,
                request.ExpectedVersion, now)) return await MapAsync(actor, lead, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "crm.lead_activity_added", "LeadActivity", lead.Id, null,
            JsonSerializer.Serialize(new { request.Type, request.Direction, request.MeaningfulContact }),
            correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapAsync(actor, lead, cancellationToken);
    }

    public async Task<LeadResponse> CompleteActivityAsync(ActorContext actor, Guid leadId, Guid activityId,
        CompleteLeadActivityRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmLeadsWork);
        var lead = await FindLeadAsync(actor, leadId, cancellationToken); var now = timeProvider.GetUtcNow();
        if (!lead.CompleteActivity(request.CommandId, activityId, actor.UserId, request.ExpectedVersion, now))
            return await MapAsync(actor, lead, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "crm.lead_activity_completed", "LeadActivity", activityId,
            null, "{}", correlationId, now); await store.SaveChangesAsync(cancellationToken);
        return await MapAsync(actor, lead, cancellationToken);
    }

    public async Task<LeadResponse> QualifyAsync(ActorContext actor, Guid leadId, QualifyLeadRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmLeadsWork); var lead = await FindLeadAsync(actor, leadId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!lead.Qualify(request.CommandId, actor.UserId, request.NextAction, request.NextActionDueAt,
                request.ExpectedVersion, now)) return await MapAsync(actor, lead, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "crm.lead_qualified", "Lead", lead.Id, null,
            JsonSerializer.Serialize(new { request.NextActionDueAt }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapAsync(actor, lead, cancellationToken);
    }

    public async Task<LeadResponse> CloseAsync(ActorContext actor, Guid leadId, CloseLeadRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmLeadsWork); var lead = await FindLeadAsync(actor, leadId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!lead.Close(request.CommandId, request.Status, actor.UserId, request.Reason, request.ExpectedVersion,
                now)) return await MapAsync(actor, lead, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "crm.lead_closed", "Lead", lead.Id, null,
            JsonSerializer.Serialize(new { request.Status }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapAsync(actor, lead, cancellationToken);
    }

    private async Task<LeadResponse> AssignCoreAsync(ActorContext actor, Guid leadId, Guid commandId,
        long expectedVersion, Guid managerUserId, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.CrmLeadsAssign); var lead = await FindLeadAsync(actor, leadId, cancellationToken);
        var eligible = await store.ListAssignableManagersAsync(actor.OrganizationId, lead.BranchId, cancellationToken);
        if (eligible.All(x => x.Id != managerUserId))
            throw new DomainException("crm.manager_unavailable", "Пользователь недоступен для назначения в этом филиале.");
        var now = timeProvider.GetUtcNow();
        if (!lead.Assign(commandId, managerUserId, actor.UserId, expectedVersion, now))
            return await MapAsync(actor, lead, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "crm.lead_assigned", "Lead", lead.Id, null,
            JsonSerializer.Serialize(new { managerUserId }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapAsync(actor, lead, cancellationToken);
    }

    private async Task<Customer> FindCustomerAsync(ActorContext actor, Guid customerId,
        CancellationToken cancellationToken)
    {
        var customer = await store.FindCustomerAsync(actor.OrganizationId, customerId, cancellationToken)
            ?? throw new NotFoundException("Customer не найден.");
        DemandBranch(actor, customer.CreatedInBranchId);
        return customer;
    }
    private async Task<Lead> FindLeadAsync(ActorContext actor, Guid leadId, CancellationToken cancellationToken)
    {
        var lead = await store.FindLeadAsync(actor.OrganizationId, leadId, cancellationToken)
            ?? throw new NotFoundException("Lead не найден."); DemandBranch(actor, lead.BranchId); return lead;
    }
    private async Task<LeadResponse> MapAsync(ActorContext actor, Lead lead, CancellationToken cancellationToken)
    {
        var customer = await store.FindCustomerAsync(actor.OrganizationId, lead.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer не найден.");
        var manager = lead.AssignedManagerUserId is { } managerId
            ? await store.FindUserAsync(actor.OrganizationId, managerId, cancellationToken) : null;
        var now = timeProvider.GetUtcNow();
        return new LeadResponse(lead.Id, lead.BranchId, lead.CustomerId, customer.Name, lead.VehicleId,
            lead.SearchCriteria, lead.Source, lead.AssignedManagerUserId, manager?.DisplayName,
            lead.Status.ToString(), lead.CreatedAt, lead.AssignedAt, lead.FirstResponseDueAt, lead.FirstResponseAt,
            lead.IsSlaBreached(now), lead.LostReason, lead.NextAction, lead.NextActionDueAt, lead.Version,
            lead.Activities.OrderBy(x => x.CreatedAt).Select(x => new LeadActivityResponse(x.Id, x.Type.ToString(),
                x.Direction.ToString(), x.Result, x.Summary, x.ActorUserId, x.IsClosed, x.CreatedAt,
                x.CompletedAt)).ToArray(), lead.History.OrderBy(x => x.OccurredAt).Select(x =>
                new LeadHistoryResponse(x.CommandId, x.FromStatus?.ToString(), x.ToStatus.ToString(), x.Operation,
                    x.OccurredAt)).ToArray());
    }
    private static CustomerResponse Map(Customer x) => new(x.Id, x.CreatedInBranchId, x.Type.ToString(), x.Name,
        x.NormalizedPhone, x.NormalizedEmail, x.PreferredChannel.ToString(), x.ConsentGiven, x.MarketingConsent,
        x.ConsentAt, x.ConsentSource, x.IsMerged, x.MergedIntoCustomerId, x.Version, x.CreatedAt);
    private static void Demand(ActorContext actor, string permission)
    { if (!actor.Permissions.Contains(permission)) throw new ForbiddenException("Недостаточно прав для CRM."); }
    private static void DemandBranch(ActorContext actor, Guid branchId)
    { if (!actor.BranchIds.Contains(branchId)) throw new ForbiddenException("Филиал недоступен пользователю."); }
}
