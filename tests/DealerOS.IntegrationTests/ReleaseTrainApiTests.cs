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

    [Fact]
    public async Task VisitAndOfferWorkflow_PreventsOverlapAndCreatesOneImmutableApprovedSnapshot()
    {
        await using var databaseLease = await _database.BeginTestAsync();
        await using var factory = new DealerOsApiFactory(_database.ConnectionString,
            _database.ObjectStorageEndpoint);
        using var preparer = factory.CreateClient();
        using var manager = factory.CreateClient();
        using var admin = factory.CreateClient();
        await AuthenticateAsync(preparer, "prep@volga-auto.demo");
        await AuthenticateAsync(manager, "manager@volga-auto.demo");
        await AuthenticateAsync(admin, "admin@volga-auto.demo");
        var vehicleId = await CreateReadyListingVehicleAsync(factory, preparer, manager);

        var customerCreate = await PostAndReadAsync(manager, "/api/crm/customers", new
        {
            branchId = DemoSeed.VolgaBranchId,
            type = "Individual",
            name = "Покупатель 0.7",
            phone = "+7 999 765-43-21",
            preferredChannel = "Phone",
            consentGiven = true,
            marketingConsent = false,
            consentAt = DateTimeOffset.UtcNow,
            consentSource = "Визит в салон"
        });
        var customerId = customerCreate.GetProperty("customer").GetProperty("id").GetGuid();
        var lead = await PostAndReadAsync(manager, "/api/crm/leads", new
        {
            branchId = DemoSeed.VolgaBranchId,
            customerId,
            vehicleId,
            source = "demo-channel"
        });
        var leadId = lead.GetProperty("id").GetGuid();
        lead = await PostAndReadAsync(manager, $"/api/crm/leads/{leadId}/assign", new
        {
            commandId = Guid.NewGuid(),
            managerUserId = DemoSeed.VolgaManagerUserId,
            expectedVersion = lead.GetProperty("version").GetInt64()
        });
        lead = await PostAndReadAsync(manager, $"/api/crm/leads/{leadId}/activities", new
        {
            commandId = Guid.NewGuid(),
            type = "Call",
            direction = "Outbound",
            result = "Answered",
            summary = "Согласован визит",
            meaningfulContact = true,
            nextAction = "Провести test drive",
            nextActionDueAt = DateTimeOffset.UtcNow.AddDays(2),
            expectedVersion = lead.GetProperty("version").GetInt64()
        });
        lead = await PostAndReadAsync(manager, $"/api/crm/leads/{leadId}/qualify", new
        {
            commandId = Guid.NewGuid(),
            nextAction = "Провести test drive",
            nextActionDueAt = DateTimeOffset.UtcNow.AddDays(2),
            expectedVersion = lead.GetProperty("version").GetInt64()
        });
        Assert.Equal("Qualified", lead.GetProperty("status").GetString());

        var startsAt = DateTimeOffset.UtcNow.AddDays(2);
        var endsAt = startsAt.AddHours(1);
        var visitRequests = new[] { Guid.NewGuid(), Guid.NewGuid() }.Select(visitId => manager.PostAsJsonAsync(
            "/api/sales/visits", new { visitId, leadId, startsAt, endsAt, includesTestDrive = true })).ToArray();
        var visitResponses = await Task.WhenAll(visitRequests);
        Assert.DoesNotContain(visitResponses, x => x.StatusCode == HttpStatusCode.InternalServerError);
        Assert.Single(visitResponses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(visitResponses, x => x.StatusCode == HttpStatusCode.Conflict);
        var visit = await ReadJsonAsync(visitResponses.Single(x => x.StatusCode == HttpStatusCode.OK));
        var visitId = visit.GetProperty("id").GetGuid();
        using (var prematureOffer = await manager.PostAsJsonAsync("/api/sales/offers/preview", new
        {
            leadId,
            lineItems = Array.Empty<object>(),
            discountAmount = 0m
        }))
            Assert.Equal(HttpStatusCode.BadRequest, prematureOffer.StatusCode);
        visit = await PostAndReadAsync(manager, $"/api/sales/visits/{visitId}/arrive", new
        {
            commandId = Guid.NewGuid(),
            expectedVersion = visit.GetProperty("version").GetInt64()
        });
        visit = await PostAndReadAsync(manager, $"/api/sales/visits/{visitId}/test-drive/check-out", new
        {
            commandId = Guid.NewGuid(),
            driverDocumentsChecked = true,
            issueChecklist = "Ключ, СТС и состояние сверены",
            odometerOutKm = 46_250,
            conditionOut = "Без новых повреждений",
            expectedVersion = visit.GetProperty("version").GetInt64()
        });
        visit = await PostAndReadAsync(manager, $"/api/sales/visits/{visitId}/test-drive/check-in", new
        {
            commandId = Guid.NewGuid(),
            returnChecklist = "Ключ, СТС и автомобиль возвращены",
            odometerInKm = 46_266,
            conditionIn = "Без новых повреждений",
            incidentOccurred = false,
            expectedVersion = visit.GetProperty("version").GetInt64()
        });
        visit = await PostAndReadAsync(manager, $"/api/sales/visits/{visitId}/complete", new
        {
            commandId = Guid.NewGuid(),
            result = "Клиент готов получить предложение",
            nextAction = "Согласовать скидку",
            nextActionDueAt = DateTimeOffset.UtcNow.AddDays(1),
            expectedVersion = visit.GetProperty("version").GetInt64()
        });
        Assert.Equal("Completed", visit.GetProperty("status").GetString());

        var lineId = Guid.NewGuid();
        var preview = await PostAndReadAsync(manager, "/api/sales/offers/preview", new
        {
            leadId,
            lineItems = new[] { new { id = lineId, category = "Equipment", name = "Зимние шины", amount = 20_000m, currency = "RUB" } },
            discountAmount = 100_000m
        });
        Assert.Equal(1_270_000m, preview.GetProperty("finalPriceAmount").GetDecimal());
        Assert.True(preview.GetProperty("requiresManagerApproval").GetBoolean());
        var offerId = Guid.NewGuid();
        var validUntil = DateTimeOffset.UtcNow.AddDays(10);
        var offer = await PostAndReadAsync(manager, "/api/sales/offers", new
        {
            offerId,
            leadId,
            validUntil,
            lineItems = new[] { new { id = lineId, category = "Equipment", name = "Зимние шины", amount = 20_000m, currency = "RUB" } },
            discountAmount = 100_000m
        });
        var createdLine = offer.GetProperty("lineItems")[0];
        var retryPayload = new
        {
            offerId,
            leadId,
            validUntil,
            lineItems = new[]
            {
                new
                {
                    id = createdLine.GetProperty("id").GetGuid(),
                    category = createdLine.GetProperty("category").GetString(),
                    name = createdLine.GetProperty("name").GetString(),
                    amount = createdLine.GetProperty("amount").GetDecimal(),
                    currency = createdLine.GetProperty("currency").GetString()
                }
            },
            discountAmount = 100_000m
        };
        var idempotentOffer = await PostAndReadAsync(manager, "/api/sales/offers", retryPayload);
        Assert.Equal(offer.GetProperty("id").GetGuid(), idempotentOffer.GetProperty("id").GetGuid());
        using (var conflictingOffer = await manager.PostAsJsonAsync("/api/sales/offers", new
        {
            offerId,
            leadId,
            validUntil,
            retryPayload.lineItems,
            discountAmount = 90_000m
        }))
            Assert.Equal(HttpStatusCode.Conflict, conflictingOffer.StatusCode);

        offer = await PostAndReadAsync(manager, $"/api/sales/offers/{offerId}/submit", new
        {
            commandId = Guid.NewGuid(),
            expectedVersion = offer.GetProperty("version").GetInt64()
        });
        Assert.Equal("Submitted", offer.GetProperty("status").GetString());
        using var selfApproval = await manager.PostAsJsonAsync($"/api/sales/offers/{offerId}/decision", new
        {
            decisionId = Guid.NewGuid(),
            decision = "Approved",
            reason = "Собственное решение",
            expectedVersion = offer.GetProperty("version").GetInt64()
        });
        Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);

        var submittedVersion = offer.GetProperty("version").GetInt64();
        var firstDecisionId = Guid.NewGuid();
        var secondDecisionId = Guid.NewGuid();
        var decisions = await Task.WhenAll(
            admin.PostAsJsonAsync($"/api/sales/offers/{offerId}/decision", new
            {
                decisionId = firstDecisionId,
                decision = "Approved",
                reason = "Маржа и скидка подтверждены",
                expectedVersion = submittedVersion
            }),
            admin.PostAsJsonAsync($"/api/sales/offers/{offerId}/decision", new
            {
                decisionId = secondDecisionId,
                decision = "Approved",
                reason = "Альтернативное решение",
                expectedVersion = submittedVersion
            }));
        Assert.DoesNotContain(decisions, x => x.StatusCode == HttpStatusCode.InternalServerError);
        Assert.Single(decisions, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(decisions, x => x.StatusCode == HttpStatusCode.Conflict);
        offer = await ReadJsonAsync(decisions.Single(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal("Approved", offer.GetProperty("status").GetString());
        var approvedSnapshotId = offer.GetProperty("approvedSnapshot").GetProperty("id").GetGuid();
        using var staleReject = await admin.PostAsJsonAsync($"/api/sales/offers/{offerId}/decision", new
        {
            decisionId = Guid.NewGuid(),
            decision = "Rejected",
            reason = "Конфликтующее решение",
            expectedVersion = submittedVersion
        });
        Assert.Equal(HttpStatusCode.Conflict, staleReject.StatusCode);

        var revisionId = Guid.NewGuid();
        var revision = await PostAndReadAsync(admin, $"/api/sales/offers/{offerId}/revisions", new
        {
            commandId = Guid.NewGuid(),
            revisionId,
            validUntil = DateTimeOffset.UtcNow.AddDays(14),
            expectedVersion = offer.GetProperty("version").GetInt64()
        });
        Assert.Equal(2, revision.GetProperty("revision").GetInt32());
        Assert.Equal("Draft", revision.GetProperty("status").GetString());
        offer = await GetAndReadAsync(admin, $"/api/sales/offers/{offerId}");
        Assert.Equal(approvedSnapshotId, offer.GetProperty("approvedSnapshot").GetProperty("id").GetGuid());
        Assert.Equal(1_270_000m, offer.GetProperty("approvedSnapshot").GetProperty("finalPriceAmount").GetDecimal());

        using var north = factory.CreateClient();
        await AuthenticateAsync(north, "admin@north-auto.demo");
        Assert.Equal(HttpStatusCode.NotFound, (await north.GetAsync($"/api/sales/visits/{visitId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await north.GetAsync($"/api/sales/offers/{offerId}")).StatusCode);
        Assert.Equal(1, await factory.QueryDbAsync(db => db.Visits.CountAsync(x => x.LeadId == leadId)));
        Assert.Equal(1, await factory.QueryDbAsync(db => db.ApprovedOfferSnapshots.CountAsync(x =>
            x.OfferId == offerId)));
    }

    private static async Task<Guid> CreateReadyListingVehicleAsync(DealerOsApiFactory factory, HttpClient preparer,
        HttpClient manager)
    {
        var (planId, _, _) = await CreateApprovedPlanAsync(factory);
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
            new { comment = "Выполнено для 0.7", expectedVersion = execution.GetProperty("version").GetInt64() });
        execution = await PostAndReadAsync(preparer, $"/api/operations/executions/{executionId}/complete",
            new { expectedVersion = execution.GetProperty("version").GetInt64() });
        var quality = await PostAndReadAsync(manager, $"/api/quality-checks/executions/{executionId}", null);
        quality = await PostAndReadAsync(manager, $"/api/quality-checks/{quality.GetProperty("id").GetGuid()}/pass",
            new { comment = "QC для sales", expectedVersion = quality.GetProperty("version").GetInt64() });
        Assert.Equal("Passed", quality.GetProperty("status").GetString());
        var media = new List<JsonElement>();
        foreach (var category in new[] { "Exterior", "Interior", "DamageHistory" })
            media.Add(await UploadMediaAsync(preparer, vehicleId, Guid.NewGuid(), category));
        await PostAndReadAsync(preparer,
            $"/api/vehicles/{vehicleId}/media/{media[0].GetProperty("id").GetGuid()}/cover",
            new { expectedVersion = media[0].GetProperty("version").GetInt64() });
        var listing = await PostAndReadAsync(preparer, $"/api/vehicles/{vehicleId}/listing", new { });
        using (var update = await preparer.PutAsJsonAsync($"/api/listings/{listing.GetProperty("id").GetGuid()}",
            new
            {
                commandId = Guid.NewGuid(),
                equipment = "Климат",
                advantages = "Прозрачная история",
                conditionDescription = "Подготовка и QC завершены.",
                publicPriceAmount = 1_350_000m,
                currency = "RUB",
                templateName = "DealerOS Default",
                templateVersion = 1,
                expectedVersion = listing.GetProperty("version").GetInt64()
            })) listing = await ReadJsonAsync(update);
        listing = await PostAndReadAsync(preparer, $"/api/listings/{listing.GetProperty("id").GetGuid()}/ready",
            new { commandId = Guid.NewGuid(), expectedVersion = listing.GetProperty("version").GetInt64() });
        Assert.Equal("Ready", listing.GetProperty("status").GetString());
        return vehicleId;
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
