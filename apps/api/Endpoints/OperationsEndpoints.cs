using DealerOS.Api.Infrastructure;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Operations.Application;

namespace DealerOS.Api.Endpoints;

public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/operations/executions").RequireAuthorization();

        group.MapPost("/", async (CreateExecutionRequest request, HttpContext http, OperationsService service,
            CancellationToken ct) => Results.Ok(await service.CreateAsync(ActorContextFactory.Create(http.User),
                request, http.TraceIdentifier, ct))).RequireAuthorization(Permissions.OperationsCreate)
            .WithName("CreateReconditioningExecution");

        group.MapGet("/{executionId:guid}", async (Guid executionId, HttpContext http, OperationsService service,
            CancellationToken ct) => Results.Ok(await service.GetAsync(ActorContextFactory.Create(http.User),
                executionId, ct))).RequireAuthorization(Permissions.OperationsView)
            .WithName("GetReconditioningExecution");

        group.MapPost("/{executionId:guid}/start", async (Guid executionId, StartExecutionRequest request,
            HttpContext http, OperationsService service, CancellationToken ct) => Results.Ok(await service.StartAsync(
                ActorContextFactory.Create(http.User), executionId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.OperationsEdit).WithName("StartReconditioningExecution");

        group.MapPost("/{executionId:guid}/work-orders/{workOrderId:guid}/schedule", async (Guid executionId,
            Guid workOrderId, ScheduleWorkOrderRequest request, HttpContext http, OperationsService service,
            CancellationToken ct) => Results.Ok(await service.ScheduleWorkAsync(ActorContextFactory.Create(http.User),
                executionId, workOrderId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.OperationsEdit).WithName("ScheduleExecutionWorkOrder");

        group.MapPost("/{executionId:guid}/work-orders/{workOrderId:guid}/start", async (Guid executionId,
            Guid workOrderId, StartWorkOrderRequest request, HttpContext http, OperationsService service,
            CancellationToken ct) => Results.Ok(await service.StartWorkAsync(ActorContextFactory.Create(http.User),
                executionId, workOrderId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.OperationsEdit).WithName("StartExecutionWorkOrder");

        group.MapPost("/{executionId:guid}/work-orders/{workOrderId:guid}/actuals", async (Guid executionId,
            Guid workOrderId, RecordWorkOrderActualsRequest request, HttpContext http, OperationsService service,
            CancellationToken ct) => Results.Ok(await service.RecordActualsAsync(ActorContextFactory.Create(http.User),
                executionId, workOrderId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.OperationsEdit).WithName("RecordExecutionActuals");

        group.MapPost("/{executionId:guid}/work-orders/{workOrderId:guid}/materials", async (Guid executionId,
            Guid workOrderId, AddMaterialMovementRequest request, HttpContext http, OperationsService service,
            CancellationToken ct) => Results.Ok(await service.AddMaterialAsync(ActorContextFactory.Create(http.User),
                executionId, workOrderId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.OperationsEdit).WithName("RecordExecutionMaterial");

        group.MapPost("/{executionId:guid}/work-orders/{workOrderId:guid}/block", async (Guid executionId,
            Guid workOrderId, BlockWorkOrderRequest request, HttpContext http, OperationsService service,
            CancellationToken ct) => Results.Ok(await service.BlockWorkAsync(ActorContextFactory.Create(http.User),
                executionId, workOrderId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.OperationsEdit).WithName("BlockExecutionWorkOrder");

        group.MapPost("/{executionId:guid}/work-orders/{workOrderId:guid}/resume", async (Guid executionId,
            Guid workOrderId, StartWorkOrderRequest request, HttpContext http, OperationsService service,
            CancellationToken ct) => Results.Ok(await service.ResumeWorkAsync(ActorContextFactory.Create(http.User),
                executionId, workOrderId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.OperationsEdit).WithName("ResumeExecutionWorkOrder");

        group.MapPost("/{executionId:guid}/work-orders/{workOrderId:guid}/complete", async (Guid executionId,
            Guid workOrderId, CompleteWorkOrderRequest request, HttpContext http, OperationsService service,
            CancellationToken ct) => Results.Ok(await service.CompleteWorkAsync(ActorContextFactory.Create(http.User),
                executionId, workOrderId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.OperationsComplete).WithName("CompleteExecutionWorkOrder");

        group.MapPost("/{executionId:guid}/work-orders/{workOrderId:guid}/settlement", async (Guid executionId,
            Guid workOrderId, SetContractorSettlementRequest request, HttpContext http, OperationsService service,
            CancellationToken ct) => Results.Ok(await service.SetSettlementAsync(ActorContextFactory.Create(http.User),
                executionId, workOrderId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.OperationsManageSettlement).WithName("SetContractorSettlement");

        group.MapPost("/{executionId:guid}/approve-overrun", async (Guid executionId,
            ApproveExecutionOverrunRequest request, HttpContext http, OperationsService service,
            CancellationToken ct) => Results.Ok(await service.ApproveOverrunAsync(
                ActorContextFactory.Create(http.User), executionId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.OperationsApproveOverrun).WithName("ApproveExecutionOverrun");

        group.MapPost("/{executionId:guid}/complete", async (Guid executionId, CompleteExecutionRequest request,
            HttpContext http, OperationsService service, CancellationToken ct) => Results.Ok(await service.CompleteAsync(
                ActorContextFactory.Create(http.User), executionId, request, http.TraceIdentifier, ct)))
            .RequireAuthorization(Permissions.OperationsComplete).WithName("CompleteReconditioningExecution");

        group.MapPost("/notifications/generate", async (HttpContext http, OperationsService service,
            CancellationToken ct) => Results.Ok(new
            {
                created = await service.GenerateNotificationsAsync(
                ActorContextFactory.Create(http.User), ct)
            }))
            .RequireAuthorization(Permissions.OperationsEdit).WithName("GenerateOperationsNotifications");

        endpoints.MapGet("/api/vehicles/{vehicleId:guid}/operations/executions", async (Guid vehicleId,
            HttpContext http, OperationsService service, CancellationToken ct) => Results.Ok(
                await service.ListForVehicleAsync(ActorContextFactory.Create(http.User), vehicleId, ct)))
            .RequireAuthorization(Permissions.OperationsView).WithName("ListVehicleExecutions");

        return endpoints;
    }
}
