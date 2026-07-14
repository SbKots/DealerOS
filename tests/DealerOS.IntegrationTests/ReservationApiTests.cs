using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DealerOS.Api.Infrastructure;
using DealerOS.Modules.Crm.Domain;
using DealerOS.Modules.Reservations.Application;
using DealerOS.Modules.Reservations.Domain;
using DealerOS.Modules.Sales.Domain;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DealerOS.IntegrationTests;

[Collection(ReconditioningPostgresCollection.Name)]
public sealed class ReservationApiTests(ReconditioningPostgresFixture database)
{
    private readonly ReconditioningPostgresFixture _database = database;
    private static readonly DateTimeOffset InitialNow = DateTimeOffset.UtcNow.AddHours(-3);

    [Fact]
    public async Task ConcurrentReservation_AllowsOneCustomerAndKeepsIdempotencyTenantAndPermissionBoundaries()
    {
        await using var databaseLease = await _database.BeginTestAsync();
        var clock = new TestTimeProvider(InitialNow);
        await using var factory = new DealerOsApiFactory(_database.ConnectionString, null, clock);
        var first = await SeedApprovedOfferAsync(factory, clock.GetUtcNow());
        var second = await SeedApprovedOfferAsync(factory, clock.GetUtcNow(), first.VehicleId);

        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        await AuthenticateAsync(firstClient, "admin@volga-auto.demo");
        await AuthenticateAsync(secondClient, "admin@volga-auto.demo");
        var firstRequest = CreateRequest(first.SnapshotId, clock.GetUtcNow().AddDays(1));
        var secondRequest = CreateRequest(second.SnapshotId, clock.GetUtcNow().AddDays(1));

        var responses = await Task.WhenAll(firstClient.PostAsJsonAsync("/api/reservations", firstRequest),
            secondClient.PostAsJsonAsync("/api/reservations", secondRequest));
        Assert.DoesNotContain(responses, x => x.StatusCode == HttpStatusCode.InternalServerError);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var winner = responses.Single(x => x.StatusCode == HttpStatusCode.OK);
        var reservation = await ReadJsonAsync(winner);
        var winningRequest = ReferenceEquals(winner, responses[0]) ? firstRequest : secondRequest;
        Assert.Equal("Active", reservation.GetProperty("status").GetString());

        using var repeated = await firstClient.PostAsJsonAsync("/api/reservations", winningRequest);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(reservation.GetProperty("id").GetGuid(),
            (await ReadJsonAsync(repeated)).GetProperty("id").GetGuid());
        Assert.Equal(1, await factory.QueryDbAsync(db => db.Reservations.CountAsync(x =>
            x.VehicleId == first.VehicleId)));
        Assert.Equal(VehicleStatus.Reserved, await factory.QueryDbAsync(db => db.Vehicles.Where(x =>
            x.Id == first.VehicleId).Select(x => x.Status).SingleAsync()));

        using var conflictingReplay = await firstClient.PostAsJsonAsync("/api/reservations", new
        {
            reservationId = winningRequest.ReservationId,
            commandId = winningRequest.CommandId,
            approvedOfferSnapshotId = winningRequest.ApprovedOfferSnapshotId,
            expiresAt = winningRequest.ExpiresAt.AddHours(1),
            depositRequired = winningRequest.DepositRequired,
            depositAmount = winningRequest.DepositAmount,
            currency = winningRequest.Currency
        });
        Assert.Equal(HttpStatusCode.Conflict, conflictingReplay.StatusCode);

        using var viewer = factory.CreateClient();
        await AuthenticateAsync(viewer, "viewer@volga-auto.demo");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync("/api/reservations", CreateRequest(first.SnapshotId,
                clock.GetUtcNow().AddDays(1)))).StatusCode);
        using var north = factory.CreateClient();
        await AuthenticateAsync(north, "admin@north-auto.demo");
        Assert.Equal(HttpStatusCode.NotFound,
            (await north.GetAsync($"/api/reservations/{reservation.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.True(await factory.QueryDbAsync(db => db.AuditEvents.AnyAsync(x =>
            x.Operation == "reservation.created" && x.EntityId == reservation.GetProperty("id").GetGuid())));
    }

    [Fact]
    public async Task DepositAndConcurrentExpireExtend_KeepReservationAndVehicleConsistent()
    {
        await using var databaseLease = await _database.BeginTestAsync();
        var clock = new TestTimeProvider(InitialNow);
        await using var factory = new DealerOsApiFactory(_database.ConnectionString, null, clock);
        var source = await SeedApprovedOfferAsync(factory, clock.GetUtcNow());
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@volga-auto.demo");
        var create = new
        {
            reservationId = Guid.NewGuid(),
            commandId = Guid.NewGuid(),
            approvedOfferSnapshotId = source.SnapshotId,
            expiresAt = clock.GetUtcNow().AddHours(1),
            depositRequired = true,
            depositAmount = 100_000m,
            currency = "RUB"
        };
        var reservation = await PostAndReadAsync(client, "/api/reservations", create);
        Assert.Equal("PendingDeposit", reservation.GetProperty("status").GetString());

        var depositCommand = Guid.NewGuid();
        reservation = await PostAndReadAsync(client,
            $"/api/reservations/{reservation.GetProperty("id").GetGuid()}/deposit", new
            {
                commandId = depositCommand,
                status = "Received",
                manualReference = "DEMO-DEPOSIT-001",
                expectedVersion = reservation.GetProperty("version").GetInt64()
            });
        var activeVersion = reservation.GetProperty("version").GetInt64();
        using var depositReplay = await client.PostAsJsonAsync(
            $"/api/reservations/{reservation.GetProperty("id").GetGuid()}/deposit", new
            {
                commandId = depositCommand,
                status = "Received",
                manualReference = "DEMO-DEPOSIT-001",
                expectedVersion = 1
            });
        Assert.Equal(HttpStatusCode.OK, depositReplay.StatusCode);
        Assert.Equal(activeVersion, (await ReadJsonAsync(depositReplay)).GetProperty("version").GetInt64());

        clock.Advance(TimeSpan.FromHours(2));
        var reservationId = reservation.GetProperty("id").GetGuid();
        var expire = client.PostAsJsonAsync($"/api/reservations/{reservationId}/expire",
            new { commandId = Guid.NewGuid(), expectedVersion = activeVersion });
        var extend = client.PostAsJsonAsync($"/api/reservations/{reservationId}/extend", new
        {
            commandId = Guid.NewGuid(),
            expiresAt = clock.GetUtcNow().AddDays(1),
            reason = "Клиент подтвердил перенос",
            expectedVersion = activeVersion
        });
        var results = await Task.WhenAll(expire, extend);
        Assert.DoesNotContain(results, x => x.StatusCode == HttpStatusCode.InternalServerError);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);

        reservation = await GetAndReadAsync(client, $"/api/reservations/{reservationId}");
        var vehicleStatus = await factory.QueryDbAsync(db => db.Vehicles.Where(x => x.Id == source.VehicleId)
            .Select(x => x.Status).SingleAsync());
        if (reservation.GetProperty("status").GetString() == "Expired")
            Assert.Equal(VehicleStatus.ReadyForSale, vehicleStatus);
        else
        {
            Assert.Equal("Active", reservation.GetProperty("status").GetString());
            Assert.Equal(VehicleStatus.Reserved, vehicleStatus);
        }
        Assert.Equal(1, await factory.QueryDbAsync(db => db.Reservations.CountAsync(x =>
            x.Id == reservationId)));
    }

    [Fact]
    public async Task ExpiredApprovedOffer_IsRejectedWithoutChangingVehicle()
    {
        await using var databaseLease = await _database.BeginTestAsync();
        var clock = new TestTimeProvider(InitialNow);
        await using var factory = new DealerOsApiFactory(_database.ConnectionString, null, clock);
        var source = await SeedApprovedOfferAsync(factory, clock.GetUtcNow(), validFor: TimeSpan.FromHours(1));
        clock.Advance(TimeSpan.FromHours(2));
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@volga-auto.demo");
        using var response = await client.PostAsJsonAsync("/api/reservations",
            CreateRequest(source.SnapshotId, clock.GetUtcNow().AddDays(1)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await factory.QueryDbAsync(db => db.Reservations.CountAsync(x =>
            x.VehicleId == source.VehicleId)));
        Assert.Equal(VehicleStatus.ReadyForSale, await factory.QueryDbAsync(db => db.Vehicles.Where(x =>
            x.Id == source.VehicleId).Select(x => x.Status).SingleAsync()));
    }

    [Fact]
    public async Task ExpirationJob_IsIdempotentAndReleasesOnlyItsActiveVehicle()
    {
        await using var databaseLease = await _database.BeginTestAsync();
        var clock = new TestTimeProvider(InitialNow);
        await using var factory = new DealerOsApiFactory(_database.ConnectionString, null, clock);
        var source = await SeedApprovedOfferAsync(factory, clock.GetUtcNow());
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@volga-auto.demo");
        var reservation = await PostAndReadAsync(client, "/api/reservations",
            CreateRequest(source.SnapshotId, clock.GetUtcNow().AddHours(1)));
        clock.Advance(TimeSpan.FromHours(2));

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ReservationService>();
        Assert.Equal(1, await service.ExpireDueAsync(CancellationToken.None));
        Assert.Equal(0, await service.ExpireDueAsync(CancellationToken.None));
        Assert.Equal(ReservationStatus.Expired, await factory.QueryDbAsync(db => db.Reservations.Where(x =>
            x.Id == reservation.GetProperty("id").GetGuid()).Select(x => x.Status).SingleAsync()));
        Assert.Equal(VehicleStatus.ReadyForSale, await factory.QueryDbAsync(db => db.Vehicles.Where(x =>
            x.Id == source.VehicleId).Select(x => x.Status).SingleAsync()));
        Assert.Equal(1, await factory.QueryDbAsync(db => db.AuditEvents.CountAsync(x =>
            x.Operation == "reservation.auto_expired" && x.EntityId == reservation.GetProperty("id").GetGuid())));
    }

    private static CreateReservationPayload CreateRequest(Guid snapshotId, DateTimeOffset expiresAt) => new(
        Guid.NewGuid(), Guid.NewGuid(), snapshotId, expiresAt, false, 0m, "RUB");

    private sealed record CreateReservationPayload(Guid ReservationId, Guid CommandId,
        Guid ApprovedOfferSnapshotId, DateTimeOffset ExpiresAt, bool DepositRequired, decimal DepositAmount,
        string Currency);

    private static async Task<(Guid VehicleId, Guid SnapshotId)> SeedApprovedOfferAsync(DealerOsApiFactory factory,
        DateTimeOffset now, Guid? existingVehicleId = null, TimeSpan? validFor = null)
    {
        return await factory.ExecuteDbWithResultAsync(async db =>
        {
            var vehicle = existingVehicleId is { } vehicleId
                ? await db.Vehicles.SingleAsync(x => x.Id == vehicleId)
                : Vehicle.CreateDraft(DemoSeed.VolgaOrganizationId, DemoSeed.VolgaBranchId,
                    "WVWZZZ1JZXW123456", "Volkswagen", "Passat", 2022, 42_000,
                    new Money(1_100_000m, "RUB"), now, DemoSeed.VolgaAdminUserId);
            if (existingVehicleId is null)
            {
                vehicle.AcceptToStock("MSK", now.AddMinutes(1), DemoSeed.VolgaAdminUserId);
                vehicle.BeginInspection(now.AddMinutes(2), DemoSeed.VolgaAdminUserId);
                vehicle.CompleteInspection(true, now.AddMinutes(3), DemoSeed.VolgaAdminUserId);
                vehicle.MarkReadyForSale(now.AddMinutes(4), DemoSeed.VolgaAdminUserId);
                db.Vehicles.Add(vehicle);
            }
            var customer = Customer.Create(DemoSeed.VolgaOrganizationId, DemoSeed.VolgaBranchId,
                CustomerType.Individual, $"Клиент {Guid.NewGuid():N}", null, $"{Guid.NewGuid():N}@demo.local",
                PreferredContactChannel.Email, false, false, null, null, DemoSeed.VolgaAdminUserId, now);
            var lead = Lead.Create(DemoSeed.VolgaOrganizationId, DemoSeed.VolgaBranchId, customer.Id, vehicle.Id,
                null, "reservation-integration", DemoSeed.VolgaAdminUserId, 30, now);
            var offer = SalesOffer.Create(Guid.NewGuid(), DemoSeed.VolgaOrganizationId, DemoSeed.VolgaBranchId,
                customer.Id, lead.Id, vehicle.Id, DemoSeed.VolgaAdminUserId, 1_500_000m, 1_100_000m, 100_000m,
                "RUB", now.Add(validFor ?? TimeSpan.FromDays(10)), [], 0, now);
            offer.Submit(Guid.NewGuid(), true, 50_000m, offer.Version, DemoSeed.VolgaAdminUserId,
                now.AddMinutes(1));
            db.Customers.Add(customer);
            db.Leads.Add(lead);
            db.SalesOffers.Add(offer);
            await db.SaveChangesAsync();
            return (vehicle.Id, offer.ApprovedSnapshot!.Id);
        });
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

    private static async Task<JsonElement> PostAndReadAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> GetAndReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
