using DealerOS.Api.Infrastructure;
using DealerOS.Modules.Crm.Application;
using DealerOS.Modules.IdentityAccess;

namespace DealerOS.Api.Endpoints;

public static class CrmEndpoints
{
    public static IEndpointRouteBuilder MapCrmEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customers = endpoints.MapGroup("/api/crm/customers").RequireAuthorization();
        customers.MapGet("/", async (string? query, HttpContext http, CrmService service, CancellationToken ct) =>
            Results.Ok(await service.SearchCustomersAsync(ActorContextFactory.Create(http.User), query, ct)))
            .RequireAuthorization(Permissions.CrmCustomersView).WithName("SearchCustomers");
        customers.MapPost("/", async (CreateCustomerRequest request, HttpContext http, CrmService service,
            CancellationToken ct) => Results.Ok(await service.CreateCustomerAsync(
                ActorContextFactory.Create(http.User), request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.CrmCustomersEdit).WithName("CreateCustomer");
        customers.MapGet("/{sourceCustomerId:guid}/merge-preview/{targetCustomerId:guid}", async (
            Guid sourceCustomerId, Guid targetCustomerId, HttpContext http, CrmService service,
            CancellationToken ct) => Results.Ok(await service.PreviewMergeAsync(
                ActorContextFactory.Create(http.User), sourceCustomerId, targetCustomerId, ct)))
            .RequireAuthorization(Permissions.CrmCustomersMerge).WithName("PreviewCustomerMerge");
        customers.MapPost("/{sourceCustomerId:guid}/merge", async (Guid sourceCustomerId,
            MergeCustomersRequest request, HttpContext http, CrmService service, CancellationToken ct) =>
            Results.Ok(await service.MergeAsync(ActorContextFactory.Create(http.User), sourceCustomerId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.CrmCustomersMerge)
            .WithName("MergeCustomers");

        var leads = endpoints.MapGroup("/api/crm/leads").RequireAuthorization();
        leads.MapGet("/", async (HttpContext http, CrmService service, CancellationToken ct) => Results.Ok(
            await service.ListLeadsAsync(ActorContextFactory.Create(http.User), ct)))
            .RequireAuthorization(Permissions.CrmLeadsView).WithName("ListLeads");
        leads.MapGet("/{leadId:guid}", async (Guid leadId, HttpContext http, CrmService service,
            CancellationToken ct) => Results.Ok(await service.GetLeadAsync(ActorContextFactory.Create(http.User),
                leadId, ct))).RequireAuthorization(Permissions.CrmLeadsView).WithName("GetLead");
        leads.MapPost("/", async (CreateLeadRequest request, HttpContext http, CrmService service,
            CancellationToken ct) => Results.Ok(await service.CreateLeadAsync(ActorContextFactory.Create(http.User),
                request, http.TraceIdentifier, ct))).RequireAuthorization(Permissions.CrmLeadsCreate)
            .WithName("CreateLead");
        leads.MapPost("/{leadId:guid}/assign", async (Guid leadId, AssignLeadRequest request, HttpContext http,
            CrmService service, CancellationToken ct) => Results.Ok(await service.AssignAsync(
                ActorContextFactory.Create(http.User), leadId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.CrmLeadsAssign).WithName("AssignLead");
        leads.MapPost("/{leadId:guid}/assign-round-robin", async (Guid leadId,
            RoundRobinAssignLeadRequest request, HttpContext http, CrmService service, CancellationToken ct) =>
            Results.Ok(await service.RoundRobinAssignAsync(ActorContextFactory.Create(http.User), leadId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.CrmLeadsAssign)
            .WithName("RoundRobinAssignLead");
        leads.MapPost("/{leadId:guid}/activities", async (Guid leadId, AddLeadActivityRequest request,
            HttpContext http, CrmService service, CancellationToken ct) => Results.Ok(await service.AddActivityAsync(
                ActorContextFactory.Create(http.User), leadId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.CrmLeadsWork).WithName("AddLeadActivity");
        leads.MapPost("/{leadId:guid}/activities/{activityId:guid}/complete", async (Guid leadId, Guid activityId,
            CompleteLeadActivityRequest request, HttpContext http, CrmService service, CancellationToken ct) =>
            Results.Ok(await service.CompleteActivityAsync(ActorContextFactory.Create(http.User), leadId,
                activityId, request, http.TraceIdentifier, ct))).RequireAuthorization(Permissions.CrmLeadsWork)
            .WithName("CompleteLeadActivity");
        leads.MapPost("/{leadId:guid}/qualify", async (Guid leadId, QualifyLeadRequest request, HttpContext http,
            CrmService service, CancellationToken ct) => Results.Ok(await service.QualifyAsync(
                ActorContextFactory.Create(http.User), leadId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.CrmLeadsWork).WithName("QualifyLead");
        leads.MapPost("/{leadId:guid}/close", async (Guid leadId, CloseLeadRequest request, HttpContext http,
            CrmService service, CancellationToken ct) => Results.Ok(await service.CloseAsync(
                ActorContextFactory.Create(http.User), leadId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.CrmLeadsWork).WithName("CloseLead");
        endpoints.MapGet("/api/crm/managers", async (Guid branchId, HttpContext http, CrmService service,
            CancellationToken ct) => Results.Ok(await service.ListAssignableManagersAsync(
                ActorContextFactory.Create(http.User), branchId, ct)))
            .RequireAuthorization(Permissions.CrmLeadsAssign).WithName("ListAssignableLeadManagers");
        return endpoints;
    }
}
