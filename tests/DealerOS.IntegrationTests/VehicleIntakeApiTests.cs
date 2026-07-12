using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DealerOS.Api.Infrastructure;
using DealerOS.Modules.IdentityAccess;
using DealerOS.SharedKernel;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DealerOS.IntegrationTests;

public sealed class VehicleIntakeApiTests : IAsyncLifetime
{
    private const string JwtKey = "integration-test-signing-key-with-at-least-32-characters";
    private string _connectionString = string.Empty;
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("dealeros_tests")
        .WithUsername("dealeros")
        .WithPassword("dealeros")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "dealeros"))
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Pooling = false,
            SslMode = SslMode.Disable
        }.ConnectionString;

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                _ = await command.ExecuteScalarAsync();
                _connectionString = connectionString;
                return;
            }
            catch (NpgsqlException exception) when (attempt < 10)
            {
                lastError = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(attempt * 200));
            }
        }

        throw new InvalidOperationException("PostgreSQL test container did not become ready.", lastError);
    }
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task IntakeHappyPath_IsAuditedPersistedAndIsolated()
    {
        await using var factory = new DealerOsApiFactory(_connectionString);
        using var volga = factory.CreateClient();
        await AuthenticateAsync(volga, "admin@volga-auto.demo");

        var createdResponse = await volga.PostAsJsonAsync("/api/vehicles", VehicleRequest("XTA210990Y0000001"));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await ReadJsonAsync(createdResponse);
        var vehicleId = created.GetProperty("id").GetGuid();
        Assert.Equal("IntakeDraft", created.GetProperty("status").GetString());
        Assert.Equal(1_000_000.12m, created.GetProperty("plannedPurchaseAmount").GetDecimal());

        var acceptedResponse = await volga.PostAsync($"/api/vehicles/{vehicleId}/accept-to-stock", null);
        Assert.Equal(HttpStatusCode.OK, acceptedResponse.StatusCode);
        var accepted = await ReadJsonAsync(acceptedResponse);
        Assert.Equal("InStock", accepted.GetProperty("status").GetString());
        Assert.StartsWith("MSK-", accepted.GetProperty("stockNumber").GetString());

        var persisted = await volga.GetFromJsonAsync<JsonElement>($"/api/vehicles/{vehicleId}");
        Assert.Equal("InStock", persisted.GetProperty("status").GetString());
        Assert.Equal(2, await factory.CountAuditEventsAsync(DemoSeed.VolgaOrganizationId, vehicleId));
        Assert.Equal(2, await factory.QueryDbAsync(db => db.VehicleStatusHistory.CountAsync(x => x.VehicleId == vehicleId)));

        using var north = factory.CreateClient();
        await AuthenticateAsync(north, "admin@north-auto.demo");
        Assert.Equal(HttpStatusCode.NotFound, (await north.GetAsync($"/api/vehicles/{vehicleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await north.PostAsync($"/api/vehicles/{vehicleId}/accept-to-stock", null)).StatusCode);
    }

    [Fact]
    public async Task AuthenticationPermissionsAndBranchScope_AreEnforcedServerSide()
    {
        await using var factory = new DealerOsApiFactory(_connectionString);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/vehicles")).StatusCode);

        using var viewer = factory.CreateClient();
        await AuthenticateAsync(viewer, "viewer@volga-auto.demo");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/vehicles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync("/api/vehicles", VehicleRequest("XTA210990Y0000011"))).StatusCode);

        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin, "admin@volga-auto.demo");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync("/api/vehicles", VehicleRequest("XTA210990Y0000012", DemoSeed.VolgaSecondaryBranchId))).StatusCode);
    }

    [Fact]
    public async Task InvalidInput_ReturnsProblemDetailsInsteadOfInternalErrors()
    {
        await using var factory = new DealerOsApiFactory(_connectionString);
        using var client = factory.CreateClient();

        var loginWithoutEmail = await client.PostAsJsonAsync("/api/auth/login", new { password = "x" });
        await AssertProblemAsync(loginWithoutEmail, HttpStatusCode.BadRequest);

        await AuthenticateAsync(client, "admin@volga-auto.demo");
        object[] invalidRequests =
        {
            new { branchId = DemoSeed.VolgaBranchId, vin = (string?)null, make = "Lada", model = "Vesta", year = 2022, mileageKm = 1000, plannedPurchaseAmount = 1_000_000m, currency = "RUB" },
            new { branchId = DemoSeed.VolgaBranchId, vin = "XTA210990Y0000021", make = "Lada", model = "Vesta", year = 2022, mileageKm = 1000, plannedPurchaseAmount = 1_000_000m, currency = (string?)null },
            new { branchId = DemoSeed.VolgaBranchId, vin = "XTA210990Y0000025", make = (string?)null, model = "Vesta", year = 2022, mileageKm = 1000, plannedPurchaseAmount = 1_000_000m, currency = "RUB" },
            new { branchId = DemoSeed.VolgaBranchId, vin = "XTA210990Y0000022", make = new string('A', 101), model = "Vesta", year = 2022, mileageKm = 1000, plannedPurchaseAmount = 1_000_000m, currency = "RUB" },
            new { branchId = DemoSeed.VolgaBranchId, vin = "INVALID", make = "Lada", model = "Vesta", year = 2022, mileageKm = 1000, plannedPurchaseAmount = 1_000_000m, currency = "RUB" },
            new { branchId = DemoSeed.VolgaBranchId, vin = "XTA210990Y0000023", make = "Lada", model = "Vesta", year = 2022, mileageKm = 1000, plannedPurchaseAmount = -1m, currency = "RUB" },
            new { branchId = DemoSeed.VolgaBranchId, vin = "XTA210990Y0000024", make = "Lada", model = "Vesta", year = 2022, mileageKm = 1000, plannedPurchaseAmount = Money.MaxAmount + 0.01m, currency = "RUB" },
            new { branchId = DemoSeed.VolgaBranchId, vin = "XTA210990Y0000026", make = "Lada", model = "Vesta", year = 2022, mileageKm = 1000, plannedPurchaseAmount = 1_000_000m, currency = "BTC" }
        };
        foreach (var request in invalidRequests)
        {
            await AssertProblemAsync(await client.PostAsJsonAsync("/api/vehicles", request), HttpStatusCode.BadRequest);
        }

        using var malformed = new StringContent("{bad-json", Encoding.UTF8, "application/json");
        await AssertProblemAsync(await client.PostAsync("/api/vehicles", malformed), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TenantIdCannotBeSpoofed_AndVinUniquenessIsTenantAware()
    {
        await using var factory = new DealerOsApiFactory(_connectionString);
        const string vin = "XTA210990Y0000031";
        using var volga = factory.CreateClient();
        await AuthenticateAsync(volga, "admin@volga-auto.demo");
        var spoofed = new
        {
            organizationId = DemoSeed.NorthOrganizationId,
            branchId = DemoSeed.VolgaBranchId,
            vin,
            make = "Lada",
            model = "Vesta",
            year = 2022,
            mileageKm = 1000,
            plannedPurchaseAmount = 1_000_000m,
            currency = "RUB"
        };
        var volgaResponse = await volga.PostAsJsonAsync("/api/vehicles", spoofed);
        Assert.Equal(HttpStatusCode.Created, volgaResponse.StatusCode);
        var volgaVehicleId = (await ReadJsonAsync(volgaResponse)).GetProperty("id").GetGuid();
        Assert.Equal(DemoSeed.VolgaOrganizationId,
            await factory.QueryDbAsync(db => db.Vehicles.Where(x => x.Id == volgaVehicleId).Select(x => x.OrganizationId).SingleAsync()));

        using var north = factory.CreateClient();
        await AuthenticateAsync(north, "admin@north-auto.demo");
        Assert.Equal(HttpStatusCode.Created,
            (await north.PostAsJsonAsync("/api/vehicles", VehicleRequest(vin, DemoSeed.NorthBranchId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await north.GetAsync($"/api/vehicles/{volgaVehicleId}")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentDuplicateVin_CreatesExactlyOneVehicle()
    {
        await using var factory = new DealerOsApiFactory(_connectionString);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@volga-auto.demo");
        const string vin = "XTA210990Y0000041";

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/vehicles", VehicleRequest(vin)),
            client.PostAsJsonAsync("/api/vehicles", VehicleRequest(vin)));

        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, await factory.QueryDbAsync(db => db.Vehicles.CountAsync(x => x.OrganizationId == DemoSeed.VolgaOrganizationId && x.Vin == vin)));
    }

    [Fact]
    public async Task ConcurrentAndRepeatedAcceptance_PreservesSingleTransition()
    {
        await using var factory = new DealerOsApiFactory(_connectionString);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@volga-auto.demo");

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var created = await client.PostAsJsonAsync("/api/vehicles", VehicleRequest($"XTA210990Y{51 + attempt:0000000}"));
            var vehicleId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

            var responses = await Task.WhenAll(
                client.PostAsync($"/api/vehicles/{vehicleId}/accept-to-stock", null),
                client.PostAsync($"/api/vehicles/{vehicleId}/accept-to-stock", null));

            Assert.DoesNotContain(responses, x => x.StatusCode == HttpStatusCode.InternalServerError);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsync($"/api/vehicles/{vehicleId}/accept-to-stock", null)).StatusCode);
            Assert.Equal(2, await factory.QueryDbAsync(db => db.VehicleStatusHistory.CountAsync(x => x.VehicleId == vehicleId)));
            Assert.Equal(2, await factory.CountAuditEventsAsync(DemoSeed.VolgaOrganizationId, vehicleId));
        }
    }

    [Fact]
    public async Task BlockAndPermissionChanges_RevokeExistingSessions()
    {
        await using var factory = new DealerOsApiFactory(_connectionString);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@volga-auto.demo");

        try
        {
            await factory.SetUserAsync(DemoSeed.VolgaAdminUserId, false, Permissions.VehicleOperator);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/vehicles")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@volga-auto.demo", password = "DealerOS!2026" })).StatusCode);

            await factory.SetUserAsync(DemoSeed.VolgaAdminUserId, true, Permissions.VehiclesRead);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/vehicles")).StatusCode);
            await AuthenticateAsync(client, "admin@volga-auto.demo");
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsJsonAsync("/api/vehicles", VehicleRequest("XTA210990Y0000061"))).StatusCode);
        }
        finally
        {
            await factory.SetUserAsync(DemoSeed.VolgaAdminUserId, true, Permissions.VehicleOperator);
        }
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        await using var factory = new DealerOsApiFactory(_connectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateExpiredToken());

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/vehicles")).StatusCode);
    }

    [Fact]
    public async Task LoginEndpoint_IsRateLimited()
    {
        await using var factory = new DealerOsApiFactory(_connectionString);
        using var client = factory.CreateClient();
        var responses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < 11; attempt++)
        {
            responses.Add(await client.PostAsJsonAsync("/api/auth/login", new { email = "missing@example.test", password = "wrong" }));
        }

        Assert.Equal(10, responses.Count(x => x.StatusCode == HttpStatusCode.Unauthorized));
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[^1].StatusCode);
    }

    [Fact]
    public async Task DatabaseConstraints_RejectCrossTenantLinksAndInvalidMoney()
    {
        await using var factory = new DealerOsApiFactory(_connectionString);
        _ = factory.CreateClient();

        var crossBranch = await Assert.ThrowsAsync<PostgresException>(() => factory.ExecuteDbAsync(db =>
            db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO identity.user_branch_access ("OrganizationId", "UserId", "BranchId")
                VALUES ('11111111-1111-4111-8111-111111111111', '11111111-1111-4111-8111-111111111001', '22222222-2222-4222-8222-222222222201')
                """)));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, crossBranch.SqlState);

        var invalidMoney = await Assert.ThrowsAsync<PostgresException>(() => factory.ExecuteDbAsync(db =>
            db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO vehicles.vehicles ("Id", "OrganizationId", "BranchId", "Vin", "Make", "Model", "Year", "MileageKm", "PlannedPurchaseAmount", "Currency", "Status", "CreatedAt", "CreatedByUserId", "Version")
                VALUES ('33333333-3333-4333-8333-333333333334', '11111111-1111-4111-8111-111111111111', '11111111-1111-4111-8111-111111111101', 'XTA210990Y0000091', 'Lada', 'Vesta', 2022, 1000, -1, 'RUB', 1, now(), '11111111-1111-4111-8111-111111111001', 1)
                """)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidMoney.SqlState);
    }

    private static object VehicleRequest(string vin, Guid? branchId = null) => new
    {
        branchId = branchId ?? DemoSeed.VolgaBranchId,
        vin,
        make = "Lada",
        model = "Vesta",
        year = 2022,
        mileageKm = 1000,
        plannedPurchaseAmount = 1_000_000.125m,
        currency = "RUB"
    };

    private static async Task AuthenticateAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "DealerOS!2026" });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(response);
        Assert.NotEqual("internal_error", body.GetProperty("code").GetString());
    }

    private static string CreateExpiredToken()
    {
        var token = new JwtSecurityToken(
            "DealerOS.Tests",
            "DealerOS.Tests",
            [new Claim("user_id", DemoSeed.VolgaAdminUserId.ToString()), new Claim("org_id", DemoSeed.VolgaOrganizationId.ToString())],
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
