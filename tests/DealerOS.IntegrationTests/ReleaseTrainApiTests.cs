using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DealerOS.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DealerOS.IntegrationTests;

[Collection(ReconditioningPostgresCollection.Name)]
public sealed class ReleaseTrainApiTests(ReconditioningPostgresFixture database)
{
    private readonly ReconditioningPostgresFixture _database = database;

    [Fact]
    public async Task ExecutionWorkflow_TracksActualCostsApprovesOverrunAndIsTenantIsolated()
    {
        await using var databaseLease = await _database.BeginTestAsync();
        await using var factory = new DealerOsApiFactory(_database.ConnectionString);
        var (PlanId, SnapshotId, Total) = await CreateApprovedPlanAsync(factory);

        using var manager = factory.CreateClient();
        await AuthenticateAsync(manager, "manager@volga-auto.demo");
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/operations/executions",
            new { planId = PlanId })).StatusCode);

        using var preparer = factory.CreateClient();
        await AuthenticateAsync(preparer, "prep@volga-auto.demo");
        using var createdResponse = await preparer.PostAsJsonAsync("/api/operations/executions",
            new { planId = PlanId, organizationId = DemoSeed.NorthOrganizationId });
        var execution = await ReadJsonAsync(createdResponse);
        var executionId = execution.GetProperty("id").GetGuid();
        var workOrderId = Assert.Single(execution.GetProperty("workOrders").EnumerateArray())
            .GetProperty("id").GetGuid();
        Assert.Equal("Draft", execution.GetProperty("status").GetString());
        Assert.Equal(Total, execution.GetProperty("plannedAmount").GetDecimal());

        using var repeatedCreate = await preparer.PostAsJsonAsync("/api/operations/executions",
            new { planId = PlanId });
        Assert.Equal(executionId, (await ReadJsonAsync(repeatedCreate)).GetProperty("id").GetGuid());

        execution = await PostAndReadAsync(preparer, $"/api/operations/executions/{executionId}/start",
            new { expectedVersion = execution.GetProperty("version").GetInt64() });
        execution = await PostAndReadAsync(preparer,
            $"/api/operations/executions/{executionId}/work-orders/{workOrderId}/start",
            new { expectedVersion = execution.GetProperty("version").GetInt64() });

        var movementId = Guid.NewGuid();
        execution = await PostAndReadAsync(preparer,
            $"/api/operations/executions/{executionId}/work-orders/{workOrderId}/materials", new
            {
                movementId,
                type = "Consumed",
                name = "Лакокрасочные материалы",
                quantity = 1m,
                unit = "комплект",
                unitCost = 5_000m,
                currency = "RUB",
                supplierName = "Демо Поставщик",
                expectedVersion = execution.GetProperty("version").GetInt64()
            });
        var versionAfterMaterial = execution.GetProperty("version").GetInt64();
        using var repeatedMaterial = await preparer.PostAsJsonAsync(
            $"/api/operations/executions/{executionId}/work-orders/{workOrderId}/materials", new
            {
                movementId,
                type = "Consumed",
                name = "Лакокрасочные материалы",
                quantity = 1m,
                unit = "комплект",
                unitCost = 5_000m,
                currency = "RUB",
                supplierName = "Демо Поставщик",
                expectedVersion = versionAfterMaterial
            });
        Assert.Equal(versionAfterMaterial, (await ReadJsonAsync(repeatedMaterial)).GetProperty("version").GetInt64());

        execution = await PostAndReadAsync(preparer,
            $"/api/operations/executions/{executionId}/work-orders/{workOrderId}/actuals", new
            {
                laborHours = 12m,
                laborAmount = 30_000m,
                externalAmount = 0m,
                expectedVersion = execution.GetProperty("version").GetInt64()
            });
        Assert.Equal(35_000m, execution.GetProperty("actualTotalAmount").GetDecimal());
        Assert.True(execution.GetProperty("varianceAmount").GetDecimal() > 0);

        execution = await PostAndReadAsync(preparer,
            $"/api/operations/executions/{executionId}/work-orders/{workOrderId}/complete",
            new { comment = "Работа завершена", expectedVersion = execution.GetProperty("version").GetInt64() });
        using var blockedCompletion = await preparer.PostAsJsonAsync(
            $"/api/operations/executions/{executionId}/complete",
            new { expectedVersion = execution.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.BadRequest, blockedCompletion.StatusCode);

        var decisionId = Guid.NewGuid();
        execution = await PostAndReadAsync(manager,
            $"/api/operations/executions/{executionId}/approve-overrun", new
            {
                decisionId,
                approvedLimitAmount = 36_000m,
                currency = "RUB",
                reason = "Подтверждён дополнительный объём материалов",
                expectedVersion = execution.GetProperty("version").GetInt64()
            });
        using var repeatedDecision = await manager.PostAsJsonAsync(
            $"/api/operations/executions/{executionId}/approve-overrun", new
            {
                decisionId,
                approvedLimitAmount = 36_000m,
                currency = "RUB",
                reason = "Подтверждён дополнительный объём материалов",
                expectedVersion = 1
            });
        Assert.Equal(HttpStatusCode.OK, repeatedDecision.StatusCode);

        execution = await PostAndReadAsync(preparer, $"/api/operations/executions/{executionId}/complete",
            new { expectedVersion = execution.GetProperty("version").GetInt64() });
        Assert.Equal("Completed", execution.GetProperty("status").GetString());
        Assert.Equal(1, await factory.QueryDbAsync(db => db.ReconditioningExecutions.CountAsync(x =>
            x.BudgetSnapshotId == SnapshotId)));
        Assert.Equal(1, await factory.QueryDbAsync(db => db.WorkOrderMaterialMovements.CountAsync(x =>
            x.Id == movementId)));
        Assert.Equal(1, await factory.QueryDbAsync(db => db.ExecutionOverrunDecisions.CountAsync(x =>
            x.Id == decisionId)));

        using var north = factory.CreateClient();
        await AuthenticateAsync(north, "admin@north-auto.demo");
        Assert.Equal(HttpStatusCode.NotFound,
            (await north.GetAsync($"/api/operations/executions/{executionId}")).StatusCode);
        Assert.True(await factory.QueryDbAsync(db => db.AuditEvents.CountAsync(x =>
            x.OrganizationId == DemoSeed.VolgaOrganizationId && x.Operation.StartsWith("operations."))) >= 6);
    }

    private static async Task<(Guid PlanId, Guid SnapshotId, decimal Total)> CreateApprovedPlanAsync(
        DealerOsApiFactory factory)
    {
        using var preparer = factory.CreateClient();
        await AuthenticateAsync(preparer, "prep@volga-auto.demo");
        var queue = await preparer.GetFromJsonAsync<JsonElement>("/api/reconditioning-plans/required-vehicles");
        var vehicle = queue.EnumerateArray().Single(x => x.GetProperty("vin").GetString() == "XTA210990Y0200002");
        var plan = await PostAndReadAsync(preparer,
            $"/api/vehicles/{vehicle.GetProperty("vehicleId").GetGuid()}/reconditioning-plans",
            new { inspectionId = vehicle.GetProperty("inspectionId").GetGuid() });
        plan = await PostAndReadAsync(preparer,
            $"/api/reconditioning-plans/{plan.GetProperty("id").GetGuid()}/submit",
            new { expectedVersion = plan.GetProperty("version").GetInt64() });
        var total = plan.GetProperty("budgets")[0].GetProperty("totalAmount").GetDecimal();

        using var manager = factory.CreateClient();
        await AuthenticateAsync(manager, "manager@volga-auto.demo");
        plan = await PostAndReadAsync(manager,
            $"/api/reconditioning-plans/{plan.GetProperty("id").GetGuid()}/approve", new
            {
                decisionId = Guid.NewGuid(),
                approvedLimitAmount = total,
                currency = "RUB",
                comment = "Утверждено для execution",
                expectedVersion = plan.GetProperty("version").GetInt64()
            });
        return (plan.GetProperty("id").GetGuid(),
            plan.GetProperty("approvedBudgetSnapshots")[0].GetProperty("id").GetGuid(), total);
    }

    private static async Task<JsonElement> PostAndReadAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        return await ReadJsonAsync(response);
    }

    private static async Task AuthenticateAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "DealerOS!2026" });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            body.GetProperty("accessToken").GetString());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
