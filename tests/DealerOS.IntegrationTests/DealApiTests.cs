using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DealerOS.Api.Infrastructure;
using DealerOS.Modules.Crm.Domain;
using DealerOS.Modules.Deals.Domain;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Reservations.Domain;
using DealerOS.Modules.Sales.Domain;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf.IO;

namespace DealerOS.IntegrationTests;

[Collection(ReconditioningPostgresCollection.Name)]
public sealed class DealApiTests(ReconditioningPostgresFixture database)
{
    private readonly ReconditioningPostgresFixture _database = database;

    [Fact]
    public async Task ReservationToSold_ReconcilesPaymentsPrivatePdfsHandoverAndUnpublishesListing()
    {
        await using var databaseLease = await _database.BeginTestAsync();
        await using var factory = new DealerOsApiFactory(_database.ConnectionString, _database.ObjectStorageEndpoint);
        var source = await SeedActiveReservationAsync(factory, false);
        using var first = factory.CreateClient(); using var second = factory.CreateClient();
        await AuthenticateAsync(first, "admin@volga-auto.demo"); await AuthenticateAsync(second, "admin@volga-auto.demo");
        var requests = new[]
        {
            new { dealId = Guid.NewGuid(), commandId = Guid.NewGuid(), reservationId = source.ReservationId },
            new { dealId = Guid.NewGuid(), commandId = Guid.NewGuid(), reservationId = source.ReservationId }
        };
        var creates = await Task.WhenAll(first.PostAsJsonAsync("/api/deals", requests[0]),
            second.PostAsJsonAsync("/api/deals", requests[1]));
        Assert.DoesNotContain(creates, x => x.StatusCode == HttpStatusCode.InternalServerError);
        Assert.Single(creates, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(creates, x => x.StatusCode == HttpStatusCode.Conflict);
        var winnerIndex = creates[0].StatusCode == HttpStatusCode.OK ? 0 : 1;
        var deal = await ReadJsonAsync(creates[winnerIndex]);
        var dealId = deal.GetProperty("id").GetGuid();
        using var replay = await first.PostAsJsonAsync("/api/deals", requests[winnerIndex]);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(dealId, (await ReadJsonAsync(replay)).GetProperty("id").GetGuid());
        Assert.Equal(ReservationStatus.ConvertedToDeal, await factory.QueryDbAsync(db => db.Reservations.Where(x =>
            x.Id == source.ReservationId).Select(x => x.Status).SingleAsync()));
        Assert.Equal(VehicleStatus.SaleInProgress, await factory.QueryDbAsync(db => db.Vehicles.Where(x =>
            x.Id == source.VehicleId).Select(x => x.Status).SingleAsync()));

        deal = await PostAndReadAsync(first, $"/api/deals/{dealId}/begin-payment",
            new { commandId = Guid.NewGuid(), expectedVersion = deal.GetProperty("version").GetInt64() });
        using (var premature = await first.PostAsJsonAsync($"/api/deals/{dealId}/ready-for-handover",
            new { commandId = Guid.NewGuid(), expectedVersion = deal.GetProperty("version").GetInt64() }))
            Assert.Equal(HttpStatusCode.BadRequest, premature.StatusCode);

        var partialId = Guid.NewGuid(); var partialCommand = Guid.NewGuid(); var occurredAt = DateTimeOffset.UtcNow;
        deal = await PostAndReadAsync(first, $"/api/deals/{dealId}/payments", new
        {
            paymentId = partialId,
            commandId = partialCommand,
            kind = "Payment",
            status = "Received",
            amount = 500_000m,
            currency = "RUB",
            manualReference = $"DEMO-PARTIAL-{dealId:N}",
            reason = "Ручная частичная оплата",
            occurredAt,
            expectedVersion = deal.GetProperty("version").GetInt64()
        });
        var versionAfterPartial = deal.GetProperty("version").GetInt64();
        using var paymentReplay = await first.PostAsJsonAsync($"/api/deals/{dealId}/payments", new
        {
            paymentId = partialId,
            commandId = partialCommand,
            kind = "Payment",
            status = "Received",
            amount = 500_000m,
            currency = "RUB",
            manualReference = $"DEMO-PARTIAL-{dealId:N}",
            reason = "Ручная частичная оплата",
            occurredAt,
            expectedVersion = 1
        });
        Assert.Equal(versionAfterPartial, (await ReadJsonAsync(paymentReplay)).GetProperty("version").GetInt64());
        deal = await PostAndReadAsync(first, $"/api/deals/{dealId}/payments", new
        {
            paymentId = Guid.NewGuid(),
            commandId = Guid.NewGuid(),
            kind = "Payment",
            status = "Received",
            amount = 1_000_000m,
            currency = "RUB",
            manualReference = $"DEMO-FINAL-{dealId:N}",
            reason = "Ручная финальная оплата",
            occurredAt = DateTimeOffset.UtcNow,
            expectedVersion = deal.GetProperty("version").GetInt64()
        });
        Assert.Equal(0m, deal.GetProperty("balance").GetDecimal());

        var documents = new List<JsonElement>();
        foreach (var type in new[] { "SaleContract", "HandoverAct" })
        {
            deal = await PostAndReadAsync(first, $"/api/deals/{dealId}/documents", new
            {
                documentId = Guid.NewGuid(),
                commandId = Guid.NewGuid(),
                type,
                expectedVersion = deal.GetProperty("version").GetInt64()
            });
            documents.Add(deal.GetProperty("documents").EnumerateArray().Single(x =>
                x.GetProperty("type").GetString() == type));
        }
        Assert.Equal(2, documents.Count);
        foreach (var document in documents)
        {
            using var download = await first.GetAsync($"/api/deals/{dealId}/documents/{document.GetProperty("id").GetGuid()}");
            download.EnsureSuccessStatusCode();
            Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
            var bytes = await download.Content.ReadAsByteArrayAsync();
            Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
            using var parsed = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
            Assert.True(parsed.PageCount > 0);
            Assert.Equal(document.GetProperty("sha256").GetString(),
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
        }
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(
            $"/api/deals/{dealId}/documents/{documents[0].GetProperty("id").GetGuid()}")).StatusCode);
        using var north = factory.CreateClient(); await AuthenticateAsync(north, "admin@north-auto.demo");
        Assert.Equal(HttpStatusCode.NotFound, (await north.GetAsync(
            $"/api/deals/{dealId}/documents/{documents[0].GetProperty("id").GetGuid()}")).StatusCode);

        deal = await PostAndReadAsync(first, $"/api/deals/{dealId}/ready-for-handover",
            new { commandId = Guid.NewGuid(), expectedVersion = deal.GetProperty("version").GetInt64() });
        using (var incomplete = await first.PostAsJsonAsync($"/api/deals/{dealId}/handover", new
        {
            commandId = Guid.NewGuid(),
            actualMileageKm = 42_100,
            keysTransferred = true,
            documentsTransferred = true,
            equipmentTransferred = false,
            conditionConfirmed = true,
            issuerConfirmed = true,
            responsibleConfirmed = true,
            conditionNotes = "Без замечаний",
            expectedVersion = deal.GetProperty("version").GetInt64()
        })) Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        deal = await PostAndReadAsync(first, $"/api/deals/{dealId}/handover", new
        {
            commandId = Guid.NewGuid(),
            actualMileageKm = 42_100,
            keysTransferred = true,
            documentsTransferred = true,
            equipmentTransferred = true,
            conditionConfirmed = true,
            issuerConfirmed = true,
            responsibleConfirmed = true,
            conditionNotes = "Автомобиль соответствует snapshot выдачи",
            comments = "Demo handover",
            expectedVersion = deal.GetProperty("version").GetInt64()
        });
        var completionVersion = deal.GetProperty("version").GetInt64();
        var completes = await Task.WhenAll(first.PostAsJsonAsync($"/api/deals/{dealId}/complete",
            new { commandId = Guid.NewGuid(), expectedVersion = completionVersion }),
            second.PostAsJsonAsync($"/api/deals/{dealId}/complete",
                new { commandId = Guid.NewGuid(), expectedVersion = completionVersion }));
        Assert.DoesNotContain(completes, x => x.StatusCode == HttpStatusCode.InternalServerError);
        Assert.Single(completes, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(completes, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(DealStatus.Completed, await factory.QueryDbAsync(db => db.Deals.Where(x => x.Id == dealId)
            .Select(x => x.Status).SingleAsync()));
        Assert.Equal(VehicleStatus.Sold, await factory.QueryDbAsync(db => db.Vehicles.Where(x =>
            x.Id == source.VehicleId).Select(x => x.Status).SingleAsync()));
        Assert.Equal(ChannelPublicationStatus.Unpublished, await factory.QueryDbAsync(db =>
            db.ChannelPublications.Where(x => x.ListingContentId == source.ListingId).Select(x => x.Status).SingleAsync()));
        Assert.Equal(2, await factory.QueryDbAsync(db => db.DealPayments.CountAsync(x => x.DealId == dealId)));
        Assert.Equal(2, await factory.QueryDbAsync(db => db.DealDocuments.CountAsync(x => x.DealId == dealId)));

        var completed = await ReadJsonAsync(completes.Single(x => x.StatusCode == HttpStatusCode.OK));
        var initialSnapshot = await factory.QueryDbAsync(db => db.ProfitSnapshots.SingleAsync(x =>
            x.DealId == dealId));
        Assert.Equal(1, initialSnapshot.Revision);
        Assert.Equal(1_500_000m, initialSnapshot.GrossRevenue);
        Assert.Equal(1_100_000m, initialSnapshot.PurchaseCost);
        Assert.Equal(400_000m, initialSnapshot.ActualProfit);

        var costId = Guid.NewGuid(); var costCommandId = Guid.NewGuid(); var costOccurredAt = DateTimeOffset.UtcNow;
        var costRequest = new
        {
            costEntryId = costId,
            commandId = costCommandId,
            category = "Advertising",
            source = "Demo marketplace",
            reference = $"ADV-{dealId:N}",
            amount = 15_000m,
            currency = "RUB",
            occurredAt = costOccurredAt,
            evidence = "demo://invoice/advertising",
            comment = "Размещение объявления"
        };
        using var costCreated = await first.PostAsJsonAsync($"/api/finance/deals/{dealId}/costs", costRequest);
        costCreated.EnsureSuccessStatusCode();
        using var costReplay = await first.PostAsJsonAsync($"/api/finance/deals/{dealId}/costs", costRequest);
        costReplay.EnsureSuccessStatusCode();
        Assert.Equal(costId, (await ReadJsonAsync(costReplay)).GetProperty("id").GetGuid());

        using var correctedResponse = await first.PostAsJsonAsync($"/api/finance/costs/{costId}/corrections", new
        {
            costEntryId = Guid.NewGuid(),
            commandId = Guid.NewGuid(),
            category = "Advertising",
            source = "Demo marketplace",
            reference = $"ADV-CORRECTED-{dealId:N}",
            amount = 20_000m,
            currency = "RUB",
            occurredAt = costOccurredAt,
            evidence = "demo://invoice/advertising-corrected",
            comment = "Исправленная сумма",
            correctionReason = "Получен итоговый акт"
        });
        correctedResponse.EnsureSuccessStatusCode();

        deal = await PostAndReadAsync(first, $"/api/deals/{dealId}/payments", new
        {
            paymentId = Guid.NewGuid(),
            commandId = Guid.NewGuid(),
            kind = "Refund",
            status = "Refunded",
            amount = 50_000m,
            currency = "RUB",
            manualReference = $"DEMO-POSTSALE-REFUND-{dealId:N}",
            reason = "Компенсация после выдачи",
            occurredAt = DateTimeOffset.UtcNow,
            expectedVersion = completed.GetProperty("version").GetInt64()
        });
        Assert.Equal("Completed", deal.GetProperty("status").GetString());
        var snapshots = await factory.QueryDbAsync(db => db.ProfitSnapshots.Where(x => x.DealId == dealId)
            .OrderBy(x => x.Revision).ToListAsync());
        Assert.Equal(4, snapshots.Count);
        Assert.Equal(initialSnapshot.Id, snapshots[1].RevisesSnapshotId);
        Assert.Equal(330_000m, snapshots[^1].ActualProfit);
        Assert.Equal(50_000m, snapshots[^1].Refunds);
        Assert.Equal(20_000m, snapshots[^1].ManualCost);

        using var economics = await first.GetAsync($"/api/finance/vehicles/{source.VehicleId}");
        economics.EnsureSuccessStatusCode();
        var economicsJson = await ReadJsonAsync(economics);
        Assert.Equal(4, economicsJson.GetProperty("revisions").GetArrayLength());
        Assert.Equal(snapshots[^1].Sha256, economicsJson.GetProperty("latest").GetProperty("sha256").GetString());

        const string correlationId = "finance-integration-correlation-001";
        using var dashboardRequest = new HttpRequestMessage(HttpMethod.Get, "/api/finance/dashboard");
        dashboardRequest.Headers.Add("X-Correlation-ID", correlationId);
        using var dashboardResponse = await first.SendAsync(dashboardRequest);
        dashboardResponse.EnsureSuccessStatusCode();
        Assert.Equal(correlationId, dashboardResponse.Headers.GetValues("X-Correlation-ID").Single());
        var dashboard = await ReadJsonAsync(dashboardResponse);
        var group = Assert.Single(dashboard.GetProperty("groups").EnumerateArray());
        Assert.Equal(330_000m, group.GetProperty("actualProfit").GetDecimal());
        Assert.Equal(1_120_000m, group.GetProperty("totalCost").GetDecimal());

        using var csvResponse = await first.GetAsync("/api/finance/export.csv");
        csvResponse.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        var csv = await csvResponse.Content.ReadAsStringAsync();
        Assert.Contains(source.VehicleId.ToString(), csv);
        Assert.DoesNotContain("Покупатель Deal", csv);
        Assert.DoesNotContain("@demo.local", csv);
        using var viewer = factory.CreateClient(); await AuthenticateAsync(viewer, "viewer@volga-auto.demo");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/finance/export.csv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await north.GetAsync($"/api/finance/vehicles/{source.VehicleId}")).StatusCode);
        Assert.True(await factory.QueryDbAsync(db => db.AuditEvents.CountAsync(x =>
            x.OrganizationId == DemoSeed.VolgaOrganizationId && x.Operation.StartsWith("finance."))) >= 3);
    }

    [Fact]
    public async Task CancellationWithDeposit_RequiresImmutableRefundBeforeVehicleRelease()
    {
        await using var databaseLease = await _database.BeginTestAsync();
        await using var factory = new DealerOsApiFactory(_database.ConnectionString, _database.ObjectStorageEndpoint);
        var source = await SeedActiveReservationAsync(factory, true);
        using var client = factory.CreateClient(); await AuthenticateAsync(client, "admin@volga-auto.demo");
        var deal = await PostAndReadAsync(client, "/api/deals", new
        { dealId = Guid.NewGuid(), commandId = Guid.NewGuid(), reservationId = source.ReservationId });
        var dealId = deal.GetProperty("id").GetGuid();
        Assert.Equal(100_000m, deal.GetProperty("receivedTotal").GetDecimal());
        deal = await PostAndReadAsync(client, $"/api/deals/{dealId}/cancel", new
        {
            commandId = Guid.NewGuid(),
            reason = "Клиент отказался от покупки",
            expectedVersion = deal.GetProperty("version").GetInt64()
        });
        Assert.Equal("RefundPending", deal.GetProperty("status").GetString());
        Assert.Equal(VehicleStatus.SaleInProgress, await factory.QueryDbAsync(db => db.Vehicles.Where(x =>
            x.Id == source.VehicleId).Select(x => x.Status).SingleAsync()));
        var refundId = Guid.NewGuid(); var refundCommand = Guid.NewGuid();
        deal = await PostAndReadAsync(client, $"/api/deals/{dealId}/payments", new
        {
            paymentId = refundId,
            commandId = refundCommand,
            kind = "Refund",
            status = "Refunded",
            amount = 100_000m,
            currency = "RUB",
            manualReference = $"DEMO-REFUND-{dealId:N}",
            reason = "Полный возврат предоплаты",
            occurredAt = DateTimeOffset.UtcNow,
            expectedVersion = deal.GetProperty("version").GetInt64()
        });
        Assert.Equal("Refunded", deal.GetProperty("status").GetString());
        Assert.Equal(0m, deal.GetProperty("netPaid").GetDecimal());
        Assert.Equal(VehicleStatus.ReadyForSale, await factory.QueryDbAsync(db => db.Vehicles.Where(x =>
            x.Id == source.VehicleId).Select(x => x.Status).SingleAsync()));
        using var repeated = await client.PostAsJsonAsync($"/api/deals/{dealId}/payments", new
        {
            paymentId = refundId,
            commandId = refundCommand,
            kind = "Refund",
            status = "Refunded",
            amount = 100_000m,
            currency = "RUB",
            manualReference = $"DEMO-REFUND-{dealId:N}",
            reason = "Полный возврат предоплаты",
            occurredAt = deal.GetProperty("payments")[1].GetProperty("occurredAt").GetDateTimeOffset(),
            expectedVersion = 1
        });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(2, await factory.QueryDbAsync(db => db.DealPayments.CountAsync(x => x.DealId == dealId)));
    }

    private static async Task<(Guid ReservationId, Guid VehicleId, Guid ListingId)> SeedActiveReservationAsync(
        DealerOsApiFactory factory, bool withDeposit)
    {
        return await factory.ExecuteDbWithResultAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var vehicle = Vehicle.CreateDraft(DemoSeed.VolgaOrganizationId, DemoSeed.VolgaBranchId,
                "WVWZZZ1JZXW654321", "Volkswagen", "Passat", 2022, 42_000,
                new Money(1_100_000m, "RUB"), now, DemoSeed.VolgaAdminUserId);
            vehicle.AcceptToStock("MSK", now.AddMinutes(1), DemoSeed.VolgaAdminUserId);
            vehicle.BeginInspection(now.AddMinutes(2), DemoSeed.VolgaAdminUserId);
            vehicle.CompleteInspection(true, now.AddMinutes(3), DemoSeed.VolgaAdminUserId);
            vehicle.MarkReadyForSale(now.AddMinutes(4), DemoSeed.VolgaAdminUserId);
            var customer = Customer.Create(DemoSeed.VolgaOrganizationId, DemoSeed.VolgaBranchId,
                CustomerType.Individual, "Покупатель Deal", null, $"{Guid.NewGuid():N}@demo.local",
                PreferredContactChannel.Email, false, false, null, null, DemoSeed.VolgaAdminUserId, now);
            var lead = Lead.Create(DemoSeed.VolgaOrganizationId, DemoSeed.VolgaBranchId, customer.Id, vehicle.Id,
                null, "deal-integration", DemoSeed.VolgaAdminUserId, 30, now);
            var offer = SalesOffer.Create(Guid.NewGuid(), DemoSeed.VolgaOrganizationId, DemoSeed.VolgaBranchId,
                customer.Id, lead.Id, vehicle.Id, DemoSeed.VolgaAdminUserId, 1_500_000m, 1_100_000m, 100_000m,
                "RUB", now.AddDays(10), [], 0, now);
            offer.Submit(Guid.NewGuid(), true, 50_000m, offer.Version, DemoSeed.VolgaAdminUserId, now.AddMinutes(5));
            var reservation = Reservation.Create(Guid.NewGuid(), DemoSeed.VolgaOrganizationId,
                DemoSeed.VolgaBranchId, vehicle.Id, customer.Id, lead.Id, offer.ApprovedSnapshot!.Id,
                DemoSeed.VolgaAdminUserId, Guid.NewGuid(), now.AddDays(2), withDeposit,
                withDeposit ? 100_000m : 0, "RUB", now.AddMinutes(6));
            vehicle.Reserve(now.AddMinutes(6), DemoSeed.VolgaAdminUserId);
            if (withDeposit) reservation.RegisterDeposit(Guid.NewGuid(), ReservationDepositStatus.Received,
                "DEMO-RESERVATION-DEPOSIT", null, reservation.Version, DemoSeed.VolgaAdminUserId,
                now.AddMinutes(7));
            var listing = ListingContent.Create(DemoSeed.VolgaOrganizationId, DemoSeed.VolgaBranchId, vehicle.Id,
                1, vehicle.Make, vehicle.Model, vehicle.Year, vehicle.MileageKm, DemoSeed.VolgaAdminUserId, now);
            listing.Update(Guid.NewGuid(), "Demo", "Demo", "Подготовлен к продаже", 1_500_000m, "RUB",
                "DealerOS Default", 1, DemoSeed.VolgaAdminUserId, listing.Version, now.AddMinutes(1));
            listing.MarkReady(Guid.NewGuid(), "{\"demo\":true}", DemoSeed.VolgaAdminUserId, listing.Version,
                now.AddMinutes(2));
            listing.Export(Guid.NewGuid(), "demo-channel", DemoSeed.VolgaAdminUserId, now.AddMinutes(3));
            listing.Publish(Guid.NewGuid(), "demo-channel", "DEMO-001", "https://example.invalid/demo",
                DemoSeed.VolgaAdminUserId, now.AddMinutes(4));
            db.Vehicles.Add(vehicle); db.Customers.Add(customer); db.Leads.Add(lead); db.SalesOffers.Add(offer);
            db.Reservations.Add(reservation); db.ListingContents.Add(listing);
            await db.SaveChangesAsync(); return (reservation.Id, vehicle.Id, listing.Id);
        });
    }

    private static async Task AuthenticateAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "DealerOS!2026" }); response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response); client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
    }
    private static async Task<JsonElement> PostAndReadAsync(HttpClient client, string path, object body)
    { using var response = await client.PostAsJsonAsync(path, body); return await ReadJsonAsync(response); }
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    { response.EnsureSuccessStatusCode(); return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone(); }
}
