using DealerOS.Api.Infrastructure;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Reconditioning.Application;

namespace DealerOS.Api.Endpoints;

public static class ReconditioningEndpoints
{
    public static IEndpointRouteBuilder MapReconditioningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reconditioning-plans").RequireAuthorization();

        group.MapGet("/required-vehicles", async (HttpContext http, ReconditioningService service,
            CancellationToken ct) => Results.Ok(await service.ListRequiredVehiclesAsync(
                ActorContextFactory.Create(http.User), ct)))
            .RequireAuthorization(Permissions.ReconditioningView).WithName("ListReconditioningRequiredVehicles");

        group.MapGet("/approvals", async (HttpContext http, ReconditioningService service, CancellationToken ct) =>
            Results.Ok(await service.ListApprovalsAsync(ActorContextFactory.Create(http.User), ct)))
            .RequireAuthorization(Permissions.ReconditioningApprove).WithName("ListReconditioningApprovals");

        group.MapGet("/{planId:guid}", async (Guid planId, HttpContext http, ReconditioningService service,
            CancellationToken ct) => Results.Ok(await service.GetAsync(ActorContextFactory.Create(http.User),
                planId, ct))).RequireAuthorization(Permissions.ReconditioningView)
            .WithName("GetReconditioningPlan");

        group.MapPost("/{planId:guid}/works", async (Guid planId, AddReconditioningWorkRequest request,
            HttpContext http, ReconditioningService service, CancellationToken ct) => Results.Ok(
                await service.AddWorkAsync(ActorContextFactory.Create(http.User), planId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ReconditioningEdit)
            .WithName("AddReconditioningWork");

        group.MapPut("/{planId:guid}/works/{workId:guid}", async (Guid planId, Guid workId,
            UpdateReconditioningWorkRequest request, HttpContext http, ReconditioningService service,
            CancellationToken ct) => Results.Ok(await service.UpdateWorkAsync(ActorContextFactory.Create(http.User),
                planId, workId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.ReconditioningEdit).WithName("UpdateReconditioningWork");

        group.MapPost("/{planId:guid}/works/{workId:guid}/remove", async (Guid planId, Guid workId,
            RemoveReconditioningWorkRequest request, HttpContext http, ReconditioningService service,
            CancellationToken ct) => Results.Ok(await service.RemoveWorkAsync(ActorContextFactory.Create(http.User),
                planId, workId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.ReconditioningEdit).WithName("RemoveReconditioningWork");

        group.MapPost("/{planId:guid}/submit", async (Guid planId, SubmitReconditioningPlanRequest request,
            HttpContext http, ReconditioningService service, CancellationToken ct) => Results.Ok(
                await service.SubmitAsync(ActorContextFactory.Create(http.User), planId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ReconditioningSubmit)
            .WithName("SubmitReconditioningPlan");

        group.MapPost("/{planId:guid}/approve", async (Guid planId, ApproveReconditioningPlanRequest request,
            HttpContext http, ReconditioningService service, CancellationToken ct) => Results.Ok(
                await service.ApproveAsync(ActorContextFactory.Create(http.User), planId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ReconditioningApprove)
            .WithName("ApproveReconditioningPlan");

        group.MapPost("/{planId:guid}/reject", async (Guid planId, RejectReconditioningPlanRequest request,
            HttpContext http, ReconditioningService service, CancellationToken ct) => Results.Ok(
                await service.RejectAsync(ActorContextFactory.Create(http.User), planId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ReconditioningApprove)
            .WithName("RejectReconditioningPlan");

        group.MapPost("/{planId:guid}/request-changes", async (Guid planId,
            RequestReconditioningChangesRequest request, HttpContext http, ReconditioningService service,
            CancellationToken ct) => Results.Ok(await service.RequestChangesAsync(
                ActorContextFactory.Create(http.User), planId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.ReconditioningApprove).WithName("RequestReconditioningChanges");

        group.MapPost("/{planId:guid}/cancel", async (Guid planId, CancelReconditioningPlanRequest request,
            HttpContext http, ReconditioningService service, CancellationToken ct) => Results.Ok(
                await service.CancelAsync(ActorContextFactory.Create(http.User), planId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ReconditioningCancel)
            .WithName("CancelReconditioningPlan");

        group.MapPost("/{planId:guid}/revisions", async (Guid planId, CreateReconditioningRevisionRequest request,
            HttpContext http, ReconditioningService service, CancellationToken ct) => Results.Ok(
                await service.CreateRevisionAsync(ActorContextFactory.Create(http.User), planId, request,
                    http.TraceIdentifier, ct))).RequireAuthorization(Permissions.ReconditioningCreate)
            .WithName("CreateReconditioningPlanRevision");

        endpoints.MapPost("/api/vehicles/{vehicleId:guid}/reconditioning-plans", async (Guid vehicleId,
            CreateReconditioningPlanRequest request, HttpContext http, ReconditioningService service,
            CancellationToken ct) => Results.Ok(await service.CreateAsync(ActorContextFactory.Create(http.User),
                vehicleId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.ReconditioningCreate).WithName("CreateVehicleReconditioningPlan");

        endpoints.MapGet("/api/vehicles/{vehicleId:guid}/reconditioning-plans", async (Guid vehicleId,
            HttpContext http, ReconditioningService service, CancellationToken ct) => Results.Ok(
                await service.ListForVehicleAsync(ActorContextFactory.Create(http.User), vehicleId, ct)))
            .RequireAuthorization(Permissions.ReconditioningView).WithName("ListVehicleReconditioningPlans");

        return endpoints;
    }
}
