using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DealerOS.Api.Infrastructure;
using DealerOS.Modules.Inspections.Domain;
using DealerOS.Modules.Reconditioning.Domain;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DealerOS.IntegrationTests;

public sealed class ReconditioningApiTests(ReconditioningPostgresFixture database)
    : IClassFixture<ReconditioningPostgresFixture>, IAsyncLifetime
{
    private readonly ReconditioningPostgresFixture _database = database;

    public Task InitializeAsync() => _database.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FullPlanWorkflow_IsAuditedApprovedImmutableAndTenantIsolated()
    {
        await using var factory = CreateFactory();
        using var preparer = factory.CreateClient();
        await AuthenticateAsync(preparer, "prep@volga-auto.demo");
        var required = await preparer.GetFromJsonAsync<JsonElement>("/api/reconditioning-plans/required-vehicles");
        var vehicle = required.EnumerateArray().Single(x => x.GetProperty("vin").GetString() == "XTA210990Y0200002");
        var vehicleId = vehicle.GetProperty("vehicleId").GetGuid();
        var inspectionId = vehicle.GetProperty("inspectionId").GetGuid();

        using var createdResponse = await preparer.PostAsJsonAsync($"/api/vehicles/{vehicleId}/reconditioning-plans",
            new { inspectionId, organizationId = DemoSeed.NorthOrganizationId });
        createdResponse.EnsureSuccessStatusCode();
        var plan = await ReadJsonAsync(createdResponse);
        var planId = plan.GetProperty("id").GetGuid();
        Assert.Equal("Draft", plan.GetProperty("status").GetString());
        var automaticWork = Assert.Single(plan.GetProperty("works").EnumerateArray());
        Assert.True(automaticWork.GetProperty("isMandatory").GetBoolean());
        Assert.Equal(32_000m, plan.GetProperty("budgets")[0].GetProperty("totalAmount").GetDecimal());

        using var updatedResponse = await preparer.PutAsJsonAsync(
            $"/api/reconditioning-plans/{planId}/works/{automaticWork.GetProperty("id").GetGuid()}", new
            {
                title = "Ремонт и окраска переднего бампера",
                description = "Восстановить крепления, подготовить и окрасить",
                category = "Body",
                priority = "High",
                isMandatory = true,
                executorType = "External",
                executorName = "Кузовной центр Партнёр",
                estimatedLaborAmount = 20_000m,
                estimatedPartsAmount = 15_000m,
                currency = "RUB",
                estimatedDurationDays = 3,
                comment = "Запись согласована предварительно",
                expectedVersion = plan.GetProperty("version").GetInt64()
            });
        updatedResponse.EnsureSuccessStatusCode();
        plan = await ReadJsonAsync(updatedResponse);
        Assert.Equal(35_000m, plan.GetProperty("budgets")[0].GetProperty("totalAmount").GetDecimal());

        using var submittedResponse = await preparer.PostAsJsonAsync(
            $"/api/reconditioning-plans/{planId}/submit",
            new { expectedVersion = plan.GetProperty("version").GetInt64() });
        submittedResponse.EnsureSuccessStatusCode();
        plan = await ReadJsonAsync(submittedResponse);
        Assert.Equal("Submitted", plan.GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.PostAsJsonAsync(
            $"/api/reconditioning-plans/{planId}/approve", new
            {
                decisionId = Guid.NewGuid(),
                approvedLimitAmount = 34_000m,
                currency = "RUB",
                expectedVersion = plan.GetProperty("version").GetInt64()
            })).StatusCode);

        using var manager = factory.CreateClient();
        await AuthenticateAsync(manager, "manager@volga-auto.demo");
        var approvals = await manager.GetFromJsonAsync<JsonElement>("/api/reconditioning-plans/approvals");
        Assert.Contains(approvals.EnumerateArray(), x => x.GetProperty("id").GetGuid() == planId);
        var decisionId = Guid.NewGuid();
        using var approvedResponse = await manager.PostAsJsonAsync($"/api/reconditioning-plans/{planId}/approve", new
        {
            decisionId,
            approvedLimitAmount = 34_000m,
            currency = "RUB",
            comment = "Согласовано в пределах лимита",
            expectedVersion = plan.GetProperty("version").GetInt64()
        });
        approvedResponse.EnsureSuccessStatusCode();
        plan = await ReadJsonAsync(approvedResponse);
        Assert.Equal("Approved", plan.GetProperty("status").GetString());
        Assert.Equal(34_000m, plan.GetProperty("approvedBudgetSnapshots")[0]
            .GetProperty("approvedLimitAmount").GetDecimal());

        using var repeated = await manager.PostAsJsonAsync($"/api/reconditioning-plans/{planId}/approve", new
        {
            decisionId,
            approvedLimitAmount = 34_000m,
            currency = "RUB",
            comment = "Согласовано в пределах лимита",
            expectedVersion = 1
        });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(1, await factory.QueryDbAsync(db => db.ReconditioningDecisions.CountAsync(x => x.PlanId == planId)));
        Assert.Equal(1, await factory.QueryDbAsync(db => db.ReconditioningBudgetSnapshots.CountAsync(x => x.PlanId == planId)));
        var queueAfterApproval = await preparer.GetFromJsonAsync<JsonElement>(
            "/api/reconditioning-plans/required-vehicles");
        var plannedVehicle = queueAfterApproval.EnumerateArray().Single(x =>
            x.GetProperty("vehicleId").GetGuid() == vehicleId);
        Assert.Equal(planId, plannedVehicle.GetProperty("latestPlanId").GetGuid());
        Assert.Equal("Approved", plannedVehicle.GetProperty("latestPlanStatus").GetString());
        Assert.False(plannedVehicle.GetProperty("hasActivePlan").GetBoolean());

        using var immutable = await preparer.PutAsJsonAsync(
            $"/api/reconditioning-plans/{planId}/works/{automaticWork.GetProperty("id").GetGuid()}", new
            {
                title = "Нельзя изменить",
                description = "Утверждённый план",
                category = "Body",
                priority = "High",
                isMandatory = true,
                executorType = "Internal",
                executorName = "Участок",
                estimatedLaborAmount = 1m,
                estimatedPartsAmount = 1m,
                currency = "RUB",
                estimatedDurationDays = 1,
                expectedVersion = plan.GetProperty("version").GetInt64()
            });
        Assert.Equal(HttpStatusCode.BadRequest, immutable.StatusCode);

        using var north = factory.CreateClient();
        await AuthenticateAsync(north, "admin@north-auto.demo");
        Assert.Equal(HttpStatusCode.NotFound,
            (await north.GetAsync($"/api/reconditioning-plans/{planId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await north.PostAsJsonAsync(
            $"/api/vehicles/{vehicleId}/reconditioning-plans", new { inspectionId })).StatusCode);
        Assert.True(await factory.QueryDbAsync(db => db.AuditEvents.CountAsync(x =>
            x.OrganizationId == DemoSeed.VolgaOrganizationId
            && x.Operation.StartsWith("reconditioning."))) >= 4);
    }

    [Fact]
    public async Task ConcurrentApproval_ProducesOneDecisionAndRevisionPreservesApprovedSource()
    {
        await using var factory = CreateFactory();
        var source = await CreateSubmittedPlanAsync(factory);
        using var managerA = factory.CreateClient();
        using var managerB = factory.CreateClient();
        await AuthenticateAsync(managerA, "manager@volga-auto.demo");
        await AuthenticateAsync(managerB, "manager@volga-auto.demo");
        var decisionId = Guid.NewGuid();
        var request = new
        {
            decisionId,
            approvedLimitAmount = source.Total,
            currency = "RUB",
            comment = "Параллельное согласование",
            expectedVersion = source.Version
        };

        var approvals = await Task.WhenAll(
            managerA.PostAsJsonAsync($"/api/reconditioning-plans/{source.PlanId}/approve", request),
            managerB.PostAsJsonAsync($"/api/reconditioning-plans/{source.PlanId}/approve", request));
        Assert.DoesNotContain(approvals, x => x.StatusCode == HttpStatusCode.InternalServerError);
        Assert.All(approvals, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        foreach (var response in approvals) response.Dispose();
        Assert.Equal(1, await factory.QueryDbAsync(db => db.ReconditioningDecisions.CountAsync(x =>
            x.PlanId == source.PlanId && x.Id == decisionId)));
        Assert.Equal(1, await factory.QueryDbAsync(db => db.ReconditioningBudgetSnapshots.CountAsync(x =>
            x.PlanId == source.PlanId)));

        using var preparer = factory.CreateClient();
        await AuthenticateAsync(preparer, "prep@volga-auto.demo");
        var approved = await preparer.GetFromJsonAsync<JsonElement>($"/api/reconditioning-plans/{source.PlanId}");
        var approvedVersion = approved.GetProperty("version").GetInt64();
        using var revisionResponse = await preparer.PostAsJsonAsync(
            $"/api/reconditioning-plans/{source.PlanId}/revisions", new { expectedVersion = approvedVersion });
        revisionResponse.EnsureSuccessStatusCode();
        var revision = await ReadJsonAsync(revisionResponse);
        Assert.Equal(2, revision.GetProperty("revision").GetInt32());
        Assert.Equal(source.PlanId, revision.GetProperty("revisesPlanId").GetGuid());
        Assert.Equal("Draft", revision.GetProperty("status").GetString());
        Assert.Equal(approved.GetProperty("works").GetArrayLength(), revision.GetProperty("works").GetArrayLength());
        Assert.Equal(0, revision.GetProperty("approvedBudgetSnapshots").GetArrayLength());

        var sourceReloaded = await preparer.GetFromJsonAsync<JsonElement>(
            $"/api/reconditioning-plans/{source.PlanId}");
        Assert.Equal("Approved", sourceReloaded.GetProperty("status").GetString());
        Assert.Equal(approvedVersion, sourceReloaded.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task ConcurrentApproval_SameDecisionIdWithDifferentPayload_ReturnsConflictForLosingCommand()
    {
        await using var factory = CreateFactory();
        var source = await CreateSubmittedPlanAsync(factory);
        using var managerA = factory.CreateClient();
        using var managerB = factory.CreateClient();
        await AuthenticateAsync(managerA, "manager@volga-auto.demo");
        await AuthenticateAsync(managerB, "manager@volga-auto.demo");
        var decisionId = Guid.NewGuid();

        var approvals = await Task.WhenAll(
            managerA.PostAsJsonAsync($"/api/reconditioning-plans/{source.PlanId}/approve", new
            {
                decisionId,
                approvedLimitAmount = source.Total,
                currency = "RUB",
                comment = "Первый лимит",
                expectedVersion = source.Version
            }),
            managerB.PostAsJsonAsync($"/api/reconditioning-plans/{source.PlanId}/approve", new
            {
                decisionId,
                approvedLimitAmount = source.Total - 1_000m,
                currency = "RUB",
                comment = "Другой лимит",
                expectedVersion = source.Version
            }));

        Assert.DoesNotContain(approvals, x => x.StatusCode == HttpStatusCode.InternalServerError);
        Assert.Single(approvals, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(approvals, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in approvals) response.Dispose();
        Assert.Equal(1, await factory.QueryDbAsync(db => db.ReconditioningDecisions.CountAsync(x =>
            x.PlanId == source.PlanId && x.Id == decisionId)));
        Assert.Equal(1, await factory.QueryDbAsync(db => db.ReconditioningBudgetSnapshots.CountAsync(x =>
            x.PlanId == source.PlanId)));
    }

    [Fact]
    public async Task PermissionsBranchAndActivePlanConstraint_AreEnforced()
    {
        await using var factory = CreateFactory();
        using var viewer = factory.CreateClient();
        await AuthenticateAsync(viewer, "viewer@volga-auto.demo");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/reconditioning-plans/required-vehicles")).StatusCode);

        var secondary = await factory.ExecuteDbWithResultAsync(async db => await AddRequiredVehicleAsync(db,
            DemoSeed.VolgaSecondaryBranchId, "XTA210990Y0200998"));
        using var preparer = factory.CreateClient();
        await AuthenticateAsync(preparer, "prep@volga-auto.demo");
        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.PostAsJsonAsync(
            $"/api/vehicles/{secondary.VehicleId}/reconditioning-plans",
            new { inspectionId = secondary.InspectionId })).StatusCode);

        var primary = await factory.ExecuteDbWithResultAsync(async db => await AddRequiredVehicleAsync(db,
            DemoSeed.VolgaBranchId, "XTA210990Y0200997"));
        var requests = await Task.WhenAll(
            preparer.PostAsJsonAsync($"/api/vehicles/{primary.VehicleId}/reconditioning-plans",
                new { inspectionId = primary.InspectionId }),
            preparer.PostAsJsonAsync($"/api/vehicles/{primary.VehicleId}/reconditioning-plans",
                new { inspectionId = primary.InspectionId }));
        Assert.DoesNotContain(requests, x => x.StatusCode == HttpStatusCode.InternalServerError);
        Assert.All(requests, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        var ids = new List<Guid>();
        foreach (var response in requests)
        {
            ids.Add((await ReadJsonAsync(response)).GetProperty("id").GetGuid());
            response.Dispose();
        }
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await factory.QueryDbAsync(db => db.ReconditioningPlans.CountAsync(x =>
            x.VehicleId == primary.VehicleId && (x.Status == ReconditioningPlanStatus.Draft
                || x.Status == ReconditioningPlanStatus.Submitted
                || x.Status == ReconditioningPlanStatus.ChangesRequested))));
    }

    [Fact]
    public async Task Migration_UpgradesFrom02AndFreshDatabaseStartsWithDemoQueue()
    {
        var options = new DbContextOptionsBuilder<DealerOsDbContext>().UseNpgsql(_database.ConnectionString).Options;
        await using (var db = new DealerOsDbContext(options))
            await db.Database.MigrateAsync("20260713083112_InspectionPhotoReliability02");

        await using var factory = CreateFactory();
        using var preparer = factory.CreateClient();
        await AuthenticateAsync(preparer, "prep@volga-auto.demo");
        var queue = await preparer.GetFromJsonAsync<JsonElement>("/api/reconditioning-plans/required-vehicles");
        Assert.Contains(queue.EnumerateArray(), x => x.GetProperty("vin").GetString() == "XTA210990Y0200002");
        Assert.True(await factory.QueryDbAsync(db => db.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'reconditioning' AND table_name = 'plans'")
            .SingleAsync()) == 1);
    }

    private DealerOsApiFactory CreateFactory() => new(_database.ConnectionString);

    private static async Task<(Guid PlanId, long Version, decimal Total)> CreateSubmittedPlanAsync(
        DealerOsApiFactory factory)
    {
        using var preparer = factory.CreateClient();
        await AuthenticateAsync(preparer, "prep@volga-auto.demo");
        var required = await preparer.GetFromJsonAsync<JsonElement>("/api/reconditioning-plans/required-vehicles");
        var vehicle = required.EnumerateArray().Single(x => x.GetProperty("vin").GetString() == "XTA210990Y0200002");
        using var created = await preparer.PostAsJsonAsync(
            $"/api/vehicles/{vehicle.GetProperty("vehicleId").GetGuid()}/reconditioning-plans",
            new { inspectionId = vehicle.GetProperty("inspectionId").GetGuid() });
        var plan = await ReadJsonAsync(created);
        using var submitted = await preparer.PostAsJsonAsync(
            $"/api/reconditioning-plans/{plan.GetProperty("id").GetGuid()}/submit",
            new { expectedVersion = plan.GetProperty("version").GetInt64() });
        plan = await ReadJsonAsync(submitted);
        return (plan.GetProperty("id").GetGuid(), plan.GetProperty("version").GetInt64(),
            plan.GetProperty("budgets")[0].GetProperty("totalAmount").GetDecimal());
    }

    private static async Task<(Guid VehicleId, Guid InspectionId)> AddRequiredVehicleAsync(DealerOsDbContext db,
        Guid branchId, string vin)
    {
        var now = DateTimeOffset.UtcNow;
        var vehicle = Vehicle.CreateDraft(DemoSeed.VolgaOrganizationId, branchId, vin, "Lada", "Niva", 2021,
            55_000, new Money(800_000m, "RUB"), now, DemoSeed.VolgaAdminUserId);
        vehicle.AcceptToStock(branchId == DemoSeed.VolgaBranchId ? "MSK" : "KZN", now,
            DemoSeed.VolgaAdminUserId);
        vehicle.BeginInspection(now, DemoSeed.VolgaInspectorUserId);
        var template = await db.InspectionTemplates.Include(x => x.Items).SingleAsync(x =>
            x.Id == DemoSeed.VolgaTemplateId);
        var inspection = Inspection.CreateDraft(DemoSeed.VolgaOrganizationId, branchId, vehicle.Id,
            DemoSeed.VolgaInspectorUserId, template, 55_000, now);
        inspection.Start(now);
        foreach (var item in inspection.Items)
            inspection.SaveItem(item.Id, InspectionItemResult.Pass, null, inspection.Version, now);
        inspection.AddDefect(Guid.NewGuid(), InspectionCategory.Brakes, "Износ тормозов", "Требуется замена",
            DefectSeverity.Major, "Заменить", 20_000m, "RUB", true, false, false, true,
            DemoSeed.VolgaInspectorUserId, inspection.Version, now);
        inspection.Complete(null, inspection.Version, now);
        vehicle.CompleteInspection(true, now, DemoSeed.VolgaInspectorUserId);
        db.Vehicles.Add(vehicle);
        db.Inspections.Add(inspection);
        await db.SaveChangesAsync();
        return (vehicle.Id, inspection.Id);
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
