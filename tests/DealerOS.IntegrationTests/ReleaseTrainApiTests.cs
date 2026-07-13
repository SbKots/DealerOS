using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DealerOS.Api.Infrastructure;
using DealerOS.Modules.Crm.Domain;
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

    [Fact]
    public async Task QualityReworkThenPass_CreatesListingSnapshotAndManualPublicationJournal()
    {
        await using var databaseLease = await _database.BeginTestAsync();
        await using var factory = new DealerOsApiFactory(_database.ConnectionString,
            _database.ObjectStorageEndpoint);
        var (planId, _, _) = await CreateApprovedPlanAsync(factory);
        using var preparer = factory.CreateClient();
        await AuthenticateAsync(preparer, "prep@volga-auto.demo");
        var execution = await PostAndReadAsync(preparer, "/api/operations/executions", new { planId });
        var executionId = execution.GetProperty("id").GetGuid();
        var vehicleId = execution.GetProperty("vehicleId").GetGuid();
        var workOrderId = execution.GetProperty("workOrders")[0].GetProperty("id").GetGuid();
        execution = await PostAndReadAsync(preparer, $"/api/operations/executions/{executionId}/start",
            new { expectedVersion = execution.GetProperty("version").GetInt64() });
        execution = await PostAndReadAsync(preparer,
            $"/api/operations/executions/{executionId}/work-orders/{workOrderId}/start",
            new { expectedVersion = execution.GetProperty("version").GetInt64() });
        execution = await PostAndReadAsync(preparer,
            $"/api/operations/executions/{executionId}/work-orders/{workOrderId}/actuals", new
            {
                laborHours = 3m,
                laborAmount = 10_000m,
                externalAmount = 0m,
                expectedVersion = execution.GetProperty("version").GetInt64()
            });
        execution = await PostAndReadAsync(preparer,
            $"/api/operations/executions/{executionId}/work-orders/{workOrderId}/complete",
            new { comment = "Первичное выполнение", expectedVersion = execution.GetProperty("version").GetInt64() });
        execution = await PostAndReadAsync(preparer, $"/api/operations/executions/{executionId}/complete",
            new { expectedVersion = execution.GetProperty("version").GetInt64() });

        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.PostAsync(
            $"/api/quality-checks/executions/{executionId}", null)).StatusCode);
        using var manager = factory.CreateClient();
        await AuthenticateAsync(manager, "manager@volga-auto.demo");
        var quality = await PostAndReadAsync(manager, $"/api/quality-checks/executions/{executionId}", null);
        var qualityId = quality.GetProperty("id").GetGuid();
        quality = await PostAndReadAsync(manager, $"/api/quality-checks/{qualityId}/observations", new
        {
            observationId = Guid.NewGuid(),
            severity = "Major",
            workOrderId,
            defectId = execution.GetProperty("workOrders")[0].GetProperty("sourceDefectId").GetGuid(),
            comment = "Следы после окраски",
            requiresRework = true,
            expectedVersion = quality.GetProperty("version").GetInt64()
        });
        quality = await PostAndReadAsync(manager, $"/api/quality-checks/{qualityId}/rework", new
        {
            comment = "Вернуть конкретную работу",
            expectedVersion = quality.GetProperty("version").GetInt64()
        });
        Assert.Equal("ReworkRequired", quality.GetProperty("status").GetString());

        execution = await GetAndReadAsync(preparer, $"/api/operations/executions/{executionId}");
        Assert.Equal("ReturnedForRework", execution.GetProperty("workOrders")[0].GetProperty("status").GetString());
        execution = await PostAndReadAsync(preparer,
            $"/api/operations/executions/{executionId}/work-orders/{workOrderId}/start",
            new { expectedVersion = execution.GetProperty("version").GetInt64() });
        execution = await PostAndReadAsync(preparer,
            $"/api/operations/executions/{executionId}/work-orders/{workOrderId}/complete",
            new { comment = "Исправлено", expectedVersion = execution.GetProperty("version").GetInt64() });
        execution = await PostAndReadAsync(preparer, $"/api/operations/executions/{executionId}/complete",
            new { expectedVersion = execution.GetProperty("version").GetInt64() });

        quality = await PostAndReadAsync(manager, $"/api/quality-checks/executions/{executionId}", null);
        Assert.Equal(2, quality.GetProperty("revision").GetInt32());
        quality = await PostAndReadAsync(manager,
            $"/api/quality-checks/{quality.GetProperty("id").GetGuid()}/pass", new
            {
                comment = "Контроль пройден",
                expectedVersion = quality.GetProperty("version").GetInt64()
            });
        Assert.Equal("Passed", quality.GetProperty("status").GetString());
        var vehicles = await preparer.GetFromJsonAsync<JsonElement>("/api/vehicles");
        Assert.Equal("ReadyForSale", vehicles.EnumerateArray().Single(x =>
            x.GetProperty("id").GetGuid() == vehicleId).GetProperty("status").GetString());

        var media = new List<JsonElement>();
        var exteriorId = Guid.NewGuid();
        var concurrentExterior = await Task.WhenAll(
            UploadMediaAsync(preparer, vehicleId, exteriorId, "Exterior"),
            UploadMediaAsync(preparer, vehicleId, exteriorId, "Exterior"));
        Assert.All(concurrentExterior, item => Assert.Equal(exteriorId, item.GetProperty("id").GetGuid()));
        media.Add(concurrentExterior[0]);
        foreach (var category in new[] { "Interior", "DamageHistory" })
            media.Add(await UploadMediaAsync(preparer, vehicleId, Guid.NewGuid(), category));
        var cover = await PostAndReadAsync(preparer,
            $"/api/vehicles/{vehicleId}/media/{media[0].GetProperty("id").GetGuid()}/cover",
            new { expectedVersion = media[0].GetProperty("version").GetInt64() });
        Assert.True(cover.GetProperty("isCover").GetBoolean());
        using var download = await preparer.GetAsync(cover.GetProperty("downloadUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);

        var listing = await PostAndReadAsync(preparer, $"/api/vehicles/{vehicleId}/listing", new { });
        using (var update = await preparer.PutAsJsonAsync($"/api/listings/{listing.GetProperty("id").GetGuid()}",
            new
            {
                commandId = Guid.NewGuid(),
                equipment = "Климат-контроль, камера",
                advantages = "Один владелец, прозрачная история",
                conditionDescription = "Следы эксплуатации раскрыты; обязательные работы завершены.",
                publicPriceAmount = 1_350_000m,
                currency = "RUB",
                templateName = "DealerOS Default",
                templateVersion = 1,
                expectedVersion = listing.GetProperty("version").GetInt64()
            })) listing = await ReadJsonAsync(update);
        listing = await PostAndReadAsync(preparer, $"/api/listings/{listing.GetProperty("id").GetGuid()}/ready",
            new { commandId = Guid.NewGuid(), expectedVersion = listing.GetProperty("version").GetInt64() });
        Assert.Equal("Ready", listing.GetProperty("status").GetString());
        Assert.DoesNotContain("DocumentsInternal", listing.GetProperty("snapshotJson").GetString());

        var exportCommandId = Guid.NewGuid();
        var export = await PostAndReadAsync(preparer,
            $"/api/listings/{listing.GetProperty("id").GetGuid()}/export",
            new { commandId = exportCommandId, channel = "demo-channel" });
        Assert.Equal("application/json", export.GetProperty("contentType").GetString());
        Assert.Contains("conditionDescription", export.GetProperty("payload").GetString());
        listing = export.GetProperty("listing");
        listing = await PostAndReadAsync(preparer,
            $"/api/listings/{listing.GetProperty("id").GetGuid()}/publish", new
            {
                commandId = Guid.NewGuid(),
                channel = "demo-channel",
                externalId = "DEMO-42",
                externalUrl = "https://example.invalid/DEMO-42"
            });
        Assert.Equal("Published", listing.GetProperty("publications")[0].GetProperty("status").GetString());

        using var north = factory.CreateClient();
        await AuthenticateAsync(north, "admin@north-auto.demo");
        Assert.Equal(HttpStatusCode.NotFound,
            (await north.GetAsync($"/api/listings/{listing.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(2, await factory.QueryDbAsync(db => db.QualityChecks.CountAsync(x =>
            x.ExecutionId == executionId)));
        Assert.Equal(3, await factory.QueryDbAsync(db => db.VehicleMedia.CountAsync(x =>
            x.VehicleId == vehicleId)));
        Assert.True(await factory.QueryDbAsync(db => db.AuditEvents.CountAsync(x =>
            x.OrganizationId == DemoSeed.VolgaOrganizationId && (x.Operation.StartsWith("quality.")
                || x.Operation.StartsWith("listing.")))) >= 8);
    }

    [Fact]
    public async Task CustomerLeadWorkflow_WarnsDuplicatesMergesExplicitlyAndMeasuresFirstResponseSla()
    {
        await using var databaseLease = await _database.BeginTestAsync();
        await using var factory = new DealerOsApiFactory(_database.ConnectionString);
        using var manager = factory.CreateClient();
        await AuthenticateAsync(manager, "manager@volga-auto.demo");

        await factory.ExecuteDbAsync(async db =>
        {
            db.Customers.Add(Customer.Create(DemoSeed.VolgaOrganizationId, DemoSeed.VolgaSecondaryBranchId,
                CustomerType.LegalEntity, "Скрытый клиент Казани", "+7 843 555-12-12", null,
                PreferredContactChannel.Phone, false, false, null, null, DemoSeed.VolgaAdminUserId,
                DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });
        var inaccessibleBranchSearch = await manager.GetFromJsonAsync<JsonElement>(
            "/api/crm/customers?query=Скрытый клиент Казани");
        Assert.Empty(inaccessibleBranchSearch.EnumerateArray());

        var targetCreate = await PostAndReadAsync(manager, "/api/crm/customers", new
        {
            branchId = DemoSeed.VolgaBranchId,
            type = "Individual",
            name = "Иван Петров",
            phone = "+7 999 123-45-67",
            email = "ivan@example.com",
            preferredChannel = "Phone",
            consentGiven = true,
            marketingConsent = false,
            consentAt = DateTimeOffset.UtcNow,
            consentSource = "Website form"
        });
        var target = targetCreate.GetProperty("customer");
        var sourceCreate = await PostAndReadAsync(manager, "/api/crm/customers", new
        {
            branchId = DemoSeed.VolgaBranchId,
            type = "Individual",
            name = "И. Петров",
            phone = "8 (999) 123-45-67",
            email = "IVAN@EXAMPLE.COM",
            preferredChannel = "Email",
            consentGiven = false,
            marketingConsent = false
        });
        var source = sourceCreate.GetProperty("customer");
        Assert.Contains(sourceCreate.GetProperty("possibleDuplicates").EnumerateArray(), x =>
            x.GetProperty("id").GetGuid() == target.GetProperty("id").GetGuid());
        Assert.Equal("+79991234567", source.GetProperty("normalizedPhone").GetString());

        var lead = await PostAndReadAsync(manager, "/api/crm/leads", new
        {
            branchId = DemoSeed.VolgaBranchId,
            customerId = source.GetProperty("id").GetGuid(),
            searchCriteria = "Кроссовер до 3 млн рублей",
            source = "Website"
        });
        var leadId = lead.GetProperty("id").GetGuid();
        var preview = await GetAndReadAsync(manager,
            $"/api/crm/customers/{source.GetProperty("id").GetGuid()}/merge-preview/{target.GetProperty("id").GetGuid()}");
        Assert.Equal(1, preview.GetProperty("leadsToMove").GetInt32());
        var mergeCommandId = Guid.NewGuid();
        var merged = await PostAndReadAsync(manager,
            $"/api/crm/customers/{source.GetProperty("id").GetGuid()}/merge", new
            {
                commandId = mergeCommandId,
                targetCustomerId = target.GetProperty("id").GetGuid(),
                reason = "Подтверждено по ivan@example.com",
                expectedSourceVersion = source.GetProperty("version").GetInt64()
            });
        Assert.True(merged.GetProperty("isMerged").GetBoolean());

        lead = await GetAndReadAsync(manager, $"/api/crm/leads/{leadId}");
        Assert.Equal(target.GetProperty("id").GetGuid(), lead.GetProperty("customerId").GetGuid());
        await factory.ExecuteDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE crm.leads SET \"FirstResponseDueAt\" = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE \"Id\" = {leadId}");
        });
        lead = await GetAndReadAsync(manager, $"/api/crm/leads/{leadId}");
        Assert.True(lead.GetProperty("slaBreached").GetBoolean());

        var assignCommandId = Guid.NewGuid();
        lead = await PostAndReadAsync(manager, $"/api/crm/leads/{leadId}/assign-round-robin", new
        {
            commandId = assignCommandId,
            expectedVersion = lead.GetProperty("version").GetInt64()
        });
        var assignedManager = lead.GetProperty("assignedManagerUserId").GetGuid();
        using var repeatedAssignment = await manager.PostAsJsonAsync($"/api/crm/leads/{leadId}/assign-round-robin",
            new { commandId = assignCommandId, expectedVersion = 1 });
        var repeatedLead = await ReadJsonAsync(repeatedAssignment);
        Assert.Equal(assignedManager, repeatedLead.GetProperty("assignedManagerUserId").GetGuid());

        var contactCommandId = Guid.NewGuid();
        lead = await PostAndReadAsync(manager, $"/api/crm/leads/{leadId}/activities", new
        {
            commandId = contactCommandId,
            type = "Call",
            direction = "Outbound",
            result = "Answered",
            summary = "Клиент подтвердил интерес",
            meaningfulContact = true,
            nextAction = "Назначить визит",
            nextActionDueAt = DateTimeOffset.UtcNow.AddDays(1),
            expectedVersion = lead.GetProperty("version").GetInt64()
        });
        var firstResponseAt = lead.GetProperty("firstResponseAt").GetDateTimeOffset();
        Assert.Equal("FirstContact", lead.GetProperty("status").GetString());
        Assert.False(lead.GetProperty("slaBreached").GetBoolean());
        using var repeatedContact = await manager.PostAsJsonAsync($"/api/crm/leads/{leadId}/activities", new
        {
            commandId = contactCommandId,
            type = "Call",
            direction = "Outbound",
            result = "Answered",
            summary = "Клиент подтвердил интерес",
            meaningfulContact = true,
            nextAction = "Назначить визит",
            nextActionDueAt = lead.GetProperty("nextActionDueAt").GetDateTimeOffset(),
            expectedVersion = 1
        });
        var repeatedFirstResponseAt = (await ReadJsonAsync(repeatedContact)).GetProperty("firstResponseAt")
            .GetDateTimeOffset();
        Assert.InRange((repeatedFirstResponseAt - firstResponseAt).Duration(), TimeSpan.Zero,
            TimeSpan.FromMicroseconds(1));
        lead = await PostAndReadAsync(manager, $"/api/crm/leads/{leadId}/qualify", new
        {
            commandId = Guid.NewGuid(),
            nextAction = "Согласовать время визита",
            nextActionDueAt = DateTimeOffset.UtcNow.AddDays(1),
            expectedVersion = lead.GetProperty("version").GetInt64()
        });
        Assert.Equal("Qualified", lead.GetProperty("status").GetString());

        using var viewer = factory.CreateClient();
        await AuthenticateAsync(viewer, "viewer@volga-auto.demo");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/crm/leads")).StatusCode);
        using var north = factory.CreateClient();
        await AuthenticateAsync(north, "admin@north-auto.demo");
        var northSearch = await north.GetFromJsonAsync<JsonElement>("/api/crm/customers?query=ivan@example.com");
        Assert.Empty(northSearch.EnumerateArray());
        var auditPayloads = await factory.QueryDbAsync(db => db.AuditEvents.Where(x =>
            x.OrganizationId == DemoSeed.VolgaOrganizationId && x.Operation.StartsWith("crm."))
            .Select(x => x.NewValue).ToListAsync());
        Assert.DoesNotContain(auditPayloads, payload => payload.Contains("ivan@example.com", StringComparison.OrdinalIgnoreCase)
            || payload.Contains("+79991234567", StringComparison.Ordinal));
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

    private static async Task<JsonElement> PostAndReadAsync(HttpClient client, string path, object? body)
    {
        using var response = body is null ? await client.PostAsync(path, null) : await client.PostAsJsonAsync(path, body);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> GetAndReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> UploadMediaAsync(HttpClient client, Guid vehicleId, Guid mediaId,
        string category)
    {
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        using var body = new MultipartFormDataContent();
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        body.Add(file, "file", $"{category}.png");
        body.Add(new StringContent(mediaId.ToString()), "mediaId");
        body.Add(new StringContent(category), "category");
        using var response = await client.PostAsync($"/api/vehicles/{vehicleId}/media", body);
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
