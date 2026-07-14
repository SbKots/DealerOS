using DealerOS.Api.Infrastructure;
using DealerOS.Modules.Finance.Application;
using DealerOS.Modules.IdentityAccess;

namespace DealerOS.Api.Endpoints;

public static class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/finance").RequireAuthorization();
        group.MapGet("/vehicles/{vehicleId:guid}", async (Guid vehicleId, HttpContext http,
            FinanceService service, CancellationToken ct) => Results.Ok(await service.GetVehicleEconomicsAsync(
            ActorContextFactory.Create(http.User), vehicleId, ct))).RequireAuthorization(Permissions.FinanceView)
            .WithName("GetVehicleEconomics");
        group.MapGet("/dashboard", async (DateTimeOffset? from, DateTimeOffset? to, Guid? branchId,
            string? currency, int? limit, HttpContext http, FinanceService service, CancellationToken ct) =>
            Results.Ok(await service.GetDashboardAsync(ActorContextFactory.Create(http.User), from, to, branchId,
                currency, limit ?? 200, ct))).RequireAuthorization(Permissions.FinanceView)
            .WithName("GetFinanceDashboard");
        group.MapGet("/export.csv", async (DateTimeOffset? from, DateTimeOffset? to, Guid? branchId,
            string? currency, HttpContext http, FinanceService service, CancellationToken ct) =>
        {
            var result = await service.ExportCsvAsync(ActorContextFactory.Create(http.User), from, to, branchId,
                currency, http.TraceIdentifier, ct);
            return Results.File(result.Content, result.ContentType, result.FileName);
        }).RequireAuthorization(Permissions.FinanceExport).WithName("ExportFinanceCsv");
        group.MapPost("/deals/{dealId:guid}/costs", async (Guid dealId, CreateManualCostRequest request,
            HttpContext http, FinanceService service, CancellationToken ct) => Results.Ok(
            await service.CreateCostAsync(ActorContextFactory.Create(http.User), dealId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.FinanceEditCosts)
            .WithName("CreateManualDealCost");
        group.MapPost("/costs/{costId:guid}/corrections", async (Guid costId, CorrectManualCostRequest request,
            HttpContext http, FinanceService service, CancellationToken ct) => Results.Ok(
            await service.CorrectCostAsync(ActorContextFactory.Create(http.User), costId, request,
                http.TraceIdentifier, ct))).RequireAuthorization(Permissions.FinanceEditCosts)
            .WithName("CorrectManualDealCost");
        return endpoints;
    }
}
