using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DealerOS.Api.Infrastructure;
using DealerOS.Modules.Inspections.Domain;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DealerOS.IntegrationTests;

public sealed class InspectionApiTests : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("dealeros_inspection_tests")
        .WithUsername("dealeros")
        .WithPassword("dealeros")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "dealeros"))
        .Build();
    private readonly IContainer _minio = new ContainerBuilder("minio/minio:RELEASE.2025-09-07T16-13-09Z")
        .WithEnvironment("MINIO_ROOT_USER", "dealer-test")
        .WithEnvironment("MINIO_ROOT_PASSWORD", "dealer-test-secret")
        .WithPortBinding(9000, true)
        .WithCommand("server", "/data")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
            .ForPort(9000).ForPath("/minio/health/live")))
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Pooling = false,
            SslMode = SslMode.Disable
        }.ConnectionString;
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
            catch (NpgsqlException) when (attempt < 10)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(attempt * 200));
            }
        }
        throw new InvalidOperationException("PostgreSQL inspection test container did not become ready.");
    }

    public async Task DisposeAsync()
    {
        await _minio.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task FullInspection_IsPersistedAuditedImmutableAndTenantIsolated()
    {
        await MigrateToPreviousReleaseAsync();
        await using var factory = CreateFactory();
        using var inspector = factory.CreateClient();
        await AuthenticateAsync(inspector, "inspector@volga-auto.demo");
        var queue = await inspector.GetFromJsonAsync<JsonElement>("/api/inspections/queue");
        var vehicle = queue.EnumerateArray().Single(x => x.GetProperty("vin").GetString() == "XTA210990Y0200001");
        var vehicleId = vehicle.GetProperty("id").GetGuid();

        var startedResponse = await inspector.PostAsJsonAsync($"/api/vehicles/{vehicleId}/inspections/start",
            new { organizationId = DemoSeed.NorthOrganizationId, mileageKm = 31_600 });
        Assert.Equal(HttpStatusCode.OK, startedResponse.StatusCode);
        var detail = await ReadJsonAsync(startedResponse);
        var inspectionId = detail.GetProperty("id").GetGuid();
        Assert.Equal("InProgress", detail.GetProperty("status").GetString());
        Assert.Equal(11, detail.GetProperty("items").GetArrayLength());

        foreach (var item in detail.GetProperty("items").EnumerateArray())
        {
            var response = await inspector.PutAsJsonAsync($"/api/inspections/{inspectionId}/items/{item.GetProperty("id").GetGuid()}",
                new { result = "Pass", comment = "Проверено", expectedVersion = detail.GetProperty("version").GetInt64() });
            response.EnsureSuccessStatusCode();
            detail = await ReadJsonAsync(response);
        }

        var defectId = Guid.NewGuid();
        var defectResponse = await inspector.PostAsJsonAsync($"/api/inspections/{inspectionId}/defects", new
        {
            defectId,
            category = "Brakes",
            title = "Трещина тормозного диска",
            description = "Требуется замена перед продажей",
            severity = "Critical",
            recommendation = "Заменить комплект",
            estimatedRepairAmount = 28_500m,
            currency = "RUB",
            repairRequired = false,
            blocksPublication = false,
            blocksTestDrive = true,
            blocksSale = false,
            expectedVersion = detail.GetProperty("version").GetInt64()
        });
        defectResponse.EnsureSuccessStatusCode();
        detail = await ReadJsonAsync(defectResponse);
        var defect = detail.GetProperty("defects")[0];
        Assert.True(defect.GetProperty("repairRequired").GetBoolean());
        Assert.True(defect.GetProperty("blocksSale").GetBoolean());

        var photoId = Guid.NewGuid();
        using var photoResponse = await UploadAsync(inspector, inspectionId, defectId, photoId,
            detail.GetProperty("version").GetInt64(), ValidPng(), "brake.png", "image/png");
        photoResponse.EnsureSuccessStatusCode();
        detail = await ReadJsonAsync(photoResponse);

        var complete = await inspector.PostAsJsonAsync($"/api/inspections/{inspectionId}/complete", new
        {
            finalComment = "Требуется подготовка",
            expectedVersion = detail.GetProperty("version").GetInt64()
        });
        complete.EnsureSuccessStatusCode();
        detail = await ReadJsonAsync(complete);
        Assert.Equal("Completed", detail.GetProperty("status").GetString());
        Assert.True(detail.GetProperty("needsReconditioning").GetBoolean());

        var vehicleAfter = await inspector.GetFromJsonAsync<JsonElement>($"/api/vehicles/{vehicleId}");
        Assert.Equal("ReconditioningRequired", vehicleAfter.GetProperty("status").GetString());
        var photo = detail.GetProperty("defects")[0].GetProperty("photos")[0];
        var downloaded = await inspector.GetAsync(photo.GetProperty("downloadUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal("image/png", downloaded.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await downloaded.Content.ReadAsByteArrayAsync());

        var immutable = await inspector.PostAsJsonAsync($"/api/inspections/{inspectionId}/defects", new
        {
            defectId = Guid.NewGuid(),
            category = "Body",
            title = "После завершения",
            description = "Нельзя",
            severity = "Minor",
            repairRequired = false,
            blocksPublication = false,
            blocksTestDrive = false,
            blocksSale = false,
            expectedVersion = detail.GetProperty("version").GetInt64()
        });
        Assert.Equal(HttpStatusCode.BadRequest, immutable.StatusCode);

        var correctionResponse = await inspector.PostAsync($"/api/inspections/{inspectionId}/corrections/start", null);
        correctionResponse.EnsureSuccessStatusCode();
        var correction = await ReadJsonAsync(correctionResponse);
        Assert.Equal(2, correction.GetProperty("revision").GetInt32());
        Assert.Equal(inspectionId, correction.GetProperty("correctsInspectionId").GetGuid());
        Assert.Equal("Completed", (await inspector.GetFromJsonAsync<JsonElement>(
            $"/api/inspections/{inspectionId}")).GetProperty("status").GetString());

        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin, "admin@volga-auto.demo");
        var templateResponse = await admin.PostAsJsonAsync("/api/inspection-templates/versions", new
        {
            name = "Новый шаблон",
            items = new[] { new { key = "new-body", category = "Body", label = "Изменённый кузов",
                description = "Новая версия", isRequired = true, sortOrder = 1 } }
        });
        Assert.Equal(HttpStatusCode.Created, templateResponse.StatusCode);
        var unchanged = await admin.GetFromJsonAsync<JsonElement>($"/api/inspections/{inspectionId}");
        Assert.Equal(1, unchanged.GetProperty("templateVersion").GetInt32());
        Assert.Equal("Кузов", unchanged.GetProperty("items")[0].GetProperty("label").GetString());

        using var north = factory.CreateClient();
        await AuthenticateAsync(north, "admin@north-auto.demo");
        Assert.Equal(HttpStatusCode.NotFound, (await north.GetAsync($"/api/inspections/{inspectionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await north.GetAsync($"/api/inspections/{inspectionId}/defects/{defectId}/photos/{photoId}")).StatusCode);

        Assert.Equal(DemoSeed.VolgaOrganizationId, await factory.QueryDbAsync(db => db.Inspections
            .Where(x => x.Id == inspectionId).Select(x => x.OrganizationId).SingleAsync()));
        Assert.True(await factory.QueryDbAsync(db => db.AuditEvents.CountAsync(x =>
            x.OrganizationId == DemoSeed.VolgaOrganizationId && x.EntityId == inspectionId)) >= 13);
    }

    [Fact]
    public async Task PermissionsBranchActiveAndConcurrentCompletion_AreEnforced()
    {
        await using var factory = CreateFactory();
        using var viewer = factory.CreateClient();
        await AuthenticateAsync(viewer, "viewer@volga-auto.demo");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/inspections/queue")).StatusCode);

        using var inspector = factory.CreateClient();
        await AuthenticateAsync(inspector, "inspector@volga-auto.demo");
        var queue = await inspector.GetFromJsonAsync<JsonElement>("/api/inspections/queue");
        var vehicleId = queue[0].GetProperty("id").GetGuid();
        var starts = await Task.WhenAll(
            inspector.PostAsJsonAsync($"/api/vehicles/{vehicleId}/inspections/start", new { mileageKm = 31_600 }),
            inspector.PostAsJsonAsync($"/api/vehicles/{vehicleId}/inspections/start", new { mileageKm = 31_600 }));
        Assert.DoesNotContain(starts, x => x.StatusCode == HttpStatusCode.InternalServerError);
        Assert.Contains(starts, x => x.StatusCode == HttpStatusCode.OK);
        Assert.All(starts, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        var inspectionId = await factory.QueryDbAsync(db => db.Inspections.Where(x => x.VehicleId == vehicleId)
            .Select(x => x.Id).SingleAsync());
        Assert.Equal(1, await factory.QueryDbAsync(db => db.Inspections.CountAsync(x => x.VehicleId == vehicleId
            && (x.Status == InspectionStatus.Draft || x.Status == InspectionStatus.InProgress))));

        var detail = await inspector.GetFromJsonAsync<JsonElement>($"/api/inspections/{inspectionId}");
        foreach (var item in detail.GetProperty("items").EnumerateArray())
        {
            var response = await inspector.PutAsJsonAsync($"/api/inspections/{inspectionId}/items/{item.GetProperty("id").GetGuid()}",
                new { result = "Pass", expectedVersion = detail.GetProperty("version").GetInt64() });
            detail = await ReadJsonAsync(response);
        }
        var expectedVersion = detail.GetProperty("version").GetInt64();
        var completions = await Task.WhenAll(
            inspector.PostAsJsonAsync($"/api/inspections/{inspectionId}/complete", new { expectedVersion }),
            inspector.PostAsJsonAsync($"/api/inspections/{inspectionId}/complete", new { expectedVersion }));
        Assert.DoesNotContain(completions, x => x.StatusCode == HttpStatusCode.InternalServerError);
        Assert.Contains(completions, x => x.StatusCode == HttpStatusCode.OK);
        Assert.All(completions, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        var repeated = await inspector.PostAsJsonAsync($"/api/inspections/{inspectionId}/complete",
            new { expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(1, await factory.QueryDbAsync(db => db.Inspections.CountAsync(x => x.Id == inspectionId
            && x.Status == InspectionStatus.Completed)));
        Assert.Equal(4, await factory.QueryDbAsync(db => db.VehicleStatusHistory.CountAsync(x => x.VehicleId == vehicleId)));
        var inspectedVehicle = await inspector.GetFromJsonAsync<JsonElement>($"/api/vehicles/{vehicleId}");
        Assert.Equal("InspectionPassed", inspectedVehicle.GetProperty("status").GetString());

        var secondaryVehicle = Vehicle.CreateDraft(DemoSeed.VolgaOrganizationId, DemoSeed.VolgaSecondaryBranchId,
            "XTA210990Y0200099", "Lada", "Granta", 2022, 20_000, new Money(700_000, "RUB"),
            DateTimeOffset.UtcNow, DemoSeed.VolgaAdminUserId);
        secondaryVehicle.AcceptToStock("KZN", DateTimeOffset.UtcNow, DemoSeed.VolgaAdminUserId);
        await factory.ExecuteDbAsync(async db => { db.Vehicles.Add(secondaryVehicle); await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Forbidden, (await inspector.PostAsJsonAsync(
            $"/api/vehicles/{secondaryVehicle.Id}/inspections/start", new { mileageKm = 20_000 })).StatusCode);
    }

    [Fact]
    public async Task ConcurrentPhotoUpload_IsIdempotentAndKeepsWinningObject()
    {
        await using var factory = CreateFactory();
        using var inspector = factory.CreateClient();
        await AuthenticateAsync(inspector, "inspector@volga-auto.demo");
        var detail = await StartInspectionAsync(inspector);
        var inspectionId = detail.GetProperty("id").GetGuid();
        var firstDefectId = Guid.NewGuid();
        detail = await AddMinorDefectAsync(inspector, inspectionId, firstDefectId,
            detail.GetProperty("version").GetInt64(), "РЎРєРѕР» РЅР° РєР°РїРѕС‚Рµ");
        var secondDefectId = Guid.NewGuid();
        detail = await AddMinorDefectAsync(inspector, inspectionId, secondDefectId,
            detail.GetProperty("version").GetInt64(), "Р¦Р°СЂР°РїРёРЅР° РЅР° РґРІРµСЂРё");

        var photoId = Guid.NewGuid();
        var expectedVersion = detail.GetProperty("version").GetInt64();
        var uploads = await Task.WhenAll(
            UploadAsync(inspector, inspectionId, firstDefectId, photoId, expectedVersion,
                ValidPng(), "concurrent-a.png", "image/png"),
            UploadAsync(inspector, inspectionId, firstDefectId, photoId, expectedVersion,
                ValidPng(), "concurrent-b.png", "image/png"));
        try
        {
            Assert.DoesNotContain(uploads, response => response.StatusCode == HttpStatusCode.InternalServerError);
            Assert.All(uploads, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        finally
        {
            foreach (var response in uploads) response.Dispose();
        }

        Assert.Equal(1, await factory.QueryDbAsync(db => db.InspectionPhotos.CountAsync(x => x.Id == photoId)));
        detail = await inspector.GetFromJsonAsync<JsonElement>($"/api/inspections/{inspectionId}");
        var photo = detail.GetProperty("defects").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == firstDefectId)
            .GetProperty("photos").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == photoId);
        using var downloaded = await inspector.GetAsync(photo.GetProperty("downloadUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.NotEmpty(await downloaded.Content.ReadAsByteArrayAsync());

        using var repeated = await UploadAsync(inspector, inspectionId, firstDefectId, photoId, expectedVersion,
            ValidPng(), "repeated.png", "image/png");
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(1, await factory.QueryDbAsync(db => db.InspectionPhotos.CountAsync(x => x.Id == photoId)));

        detail = await ReadJsonAsync(repeated);
        using var conflicting = await UploadAsync(inspector, inspectionId, secondDefectId, photoId,
            detail.GetProperty("version").GetInt64(), ValidPng(), "wrong-defect.png", "image/png");
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
    }

    [Fact]
    public async Task Correction_PreservesSharedPhotoAndSourceCompletedInspection()
    {
        await using var factory = CreateFactory();
        using var inspector = factory.CreateClient();
        await AuthenticateAsync(inspector, "inspector@volga-auto.demo");
        var detail = await StartInspectionAsync(inspector);
        var inspectionId = detail.GetProperty("id").GetGuid();
        detail = await CompleteRequiredItemsAsync(inspector, inspectionId, detail);
        var defectId = Guid.NewGuid();
        detail = await AddMinorDefectAsync(inspector, inspectionId, defectId,
            detail.GetProperty("version").GetInt64(), "Р¤РѕС‚Рѕ-РґРѕРєР°Р·Р°С‚РµР»СЊСЃС‚РІРѕ");
        var photoId = Guid.NewGuid();
        using (var upload = await UploadAsync(inspector, inspectionId, defectId, photoId,
                   detail.GetProperty("version").GetInt64(), ValidPng(), "evidence.png", "image/png"))
        {
            detail = await ReadJsonAsync(upload);
        }
        using (var completed = await inspector.PostAsJsonAsync($"/api/inspections/{inspectionId}/complete",
                   new { finalComment = "РСЃС…РѕРґРЅР°СЏ СЂРµРІРёР·РёСЏ", expectedVersion = detail.GetProperty("version").GetInt64() }))
        {
            detail = await ReadJsonAsync(completed);
        }
        var sourceCompletedVersion = detail.GetProperty("version").GetInt64();

        using var correctionStarted = await inspector.PostAsync($"/api/inspections/{inspectionId}/corrections/start", null);
        var correction = await ReadJsonAsync(correctionStarted);
        var correctionId = correction.GetProperty("id").GetGuid();
        var correctionDefect = correction.GetProperty("defects").EnumerateArray().Single();
        var correctionDefectId = correctionDefect.GetProperty("id").GetGuid();
        var correctionPhoto = correctionDefect.GetProperty("photos").EnumerateArray().Single();
        Assert.NotEqual(photoId, correctionPhoto.GetProperty("id").GetGuid());
        Assert.Equal(photoId, correctionPhoto.GetProperty("sourcePhotoId").GetGuid());
        using (var correctionDownload = await inspector.GetAsync(correctionPhoto.GetProperty("downloadUrl").GetString()))
        {
            Assert.Equal(HttpStatusCode.OK, correctionDownload.StatusCode);
            Assert.NotEmpty(await correctionDownload.Content.ReadAsByteArrayAsync());
        }

        using var removed = await inspector.DeleteAsync($"/api/inspections/{correctionId}/defects/{correctionDefectId}" +
            $"?expectedVersion={correction.GetProperty("version").GetInt64()}");
        correction = await ReadJsonAsync(removed);
        Assert.Equal(0, correction.GetProperty("defects").GetArrayLength());

        using (var sourceDownload = await inspector.GetAsync(
                   $"/api/inspections/{inspectionId}/defects/{defectId}/photos/{photoId}"))
        {
            Assert.Equal(HttpStatusCode.OK, sourceDownload.StatusCode);
            Assert.NotEmpty(await sourceDownload.Content.ReadAsByteArrayAsync());
        }

        using (var correctionCompleted = await inspector.PostAsJsonAsync($"/api/inspections/{correctionId}/complete",
                   new { finalComment = "РђРєС‚СѓР°Р»СЊРЅР°СЏ СЂРµРІРёР·РёСЏ", expectedVersion = correction.GetProperty("version").GetInt64() }))
        {
            correction = await ReadJsonAsync(correctionCompleted);
        }
        var sourceReloaded = await inspector.GetFromJsonAsync<JsonElement>($"/api/inspections/{inspectionId}");
        var correctionReloaded = await inspector.GetFromJsonAsync<JsonElement>($"/api/inspections/{correctionId}");
        Assert.Equal("Completed", sourceReloaded.GetProperty("status").GetString());
        Assert.Equal(sourceCompletedVersion, sourceReloaded.GetProperty("version").GetInt64());
        Assert.Single(sourceReloaded.GetProperty("defects")[0].GetProperty("photos").EnumerateArray());
        Assert.Equal("Completed", correctionReloaded.GetProperty("status").GetString());
        Assert.Equal(0, correctionReloaded.GetProperty("defects").GetArrayLength());
    }

    [Fact]
    public async Task RemoveDraftDefect_WhenMinioUnavailable_CommitsDbAndQueuesTenantCleanup()
    {
        await using var factory = CreateFactory();
        using var inspector = factory.CreateClient();
        await AuthenticateAsync(inspector, "inspector@volga-auto.demo");
        var detail = await StartInspectionAsync(inspector);
        var inspectionId = detail.GetProperty("id").GetGuid();
        var defectId = Guid.NewGuid();
        detail = await AddMinorDefectAsync(inspector, inspectionId, defectId,
            detail.GetProperty("version").GetInt64(), "РћСЃРёСЂРѕС‚РµРІС€РёР№ РѕР±СЉРµРєС‚");
        var photoId = Guid.NewGuid();
        using (var upload = await UploadAsync(inspector, inspectionId, defectId, photoId,
                   detail.GetProperty("version").GetInt64(), ValidPng(), "orphan.png", "image/png"))
        {
            detail = await ReadJsonAsync(upload);
        }
        var objectKey = await factory.QueryDbAsync(db => db.InspectionPhotos.Where(x => x.Id == photoId)
            .Select(x => x.ObjectKey).SingleAsync());

        await _minio.StopAsync();
        using var removed = await inspector.DeleteAsync($"/api/inspections/{inspectionId}/defects/{defectId}" +
            $"?expectedVersion={detail.GetProperty("version").GetInt64()}");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(0, await factory.QueryDbAsync(db => db.InspectionDefects.CountAsync(x => x.Id == defectId)));
        Assert.Equal(0, await factory.QueryDbAsync(db => db.InspectionPhotos.CountAsync(x => x.Id == photoId)));
        var pendingCleanup = await factory.QueryDbAsync(db => db.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM inspections.object_deletion_queue WHERE \"OrganizationId\" = {DemoSeed.VolgaOrganizationId} AND \"ObjectKey\" = {objectKey}")
            .SingleAsync());
        Assert.Equal(1, pendingCleanup);
    }

    [Fact]
    public async Task InvalidOversizedIncompleteFilesAndMinioFailure_LeaveNoPhotoMetadata()
    {
        await using var factory = CreateFactory();
        using var inspector = factory.CreateClient();
        await AuthenticateAsync(inspector, "inspector@volga-auto.demo");
        var queue = await inspector.GetFromJsonAsync<JsonElement>("/api/inspections/queue");
        var vehicleId = queue[0].GetProperty("id").GetGuid();
        var started = await inspector.PostAsJsonAsync($"/api/vehicles/{vehicleId}/inspections/start",
            new { mileageKm = 31_600 });
        var detail = await ReadJsonAsync(started);
        var inspectionId = detail.GetProperty("id").GetGuid();
        var defectId = Guid.NewGuid();
        var defectResponse = await inspector.PostAsJsonAsync($"/api/inspections/{inspectionId}/defects", new
        {
            defectId,
            category = "Body",
            title = "Царапина",
            description = "На двери",
            severity = "Minor",
            repairRequired = false,
            blocksPublication = false,
            blocksTestDrive = false,
            blocksSale = false,
            expectedVersion = detail.GetProperty("version").GetInt64()
        });
        detail = await ReadJsonAsync(defectResponse);
        var version = detail.GetProperty("version").GetInt64();

        using var fake = await UploadAsync(inspector, inspectionId, defectId, Guid.NewGuid(), version,
            Encoding.UTF8.GetBytes("not-an-image"), "spoofed.jpg", "image/jpeg");
        Assert.Equal(HttpStatusCode.BadRequest, fake.StatusCode);
        using var incomplete = await UploadAsync(inspector, inspectionId, defectId, Guid.NewGuid(), version,
            ValidPng()[..16], "truncated.png", "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        using var oversized = await UploadAsync(inspector, inspectionId, defectId, Guid.NewGuid(), version,
            new byte[InspectionImageProcessor.MaxInputBytes + 1], "large.png", "image/png");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal(0, await factory.QueryDbAsync(db => db.InspectionPhotos.CountAsync()));

        await _minio.StopAsync();
        using var unavailable = await UploadAsync(inspector, inspectionId, defectId, Guid.NewGuid(), version,
            ValidPng(), "valid.png", "image/png");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal(0, await factory.QueryDbAsync(db => db.InspectionPhotos.CountAsync()));
    }

    private DealerOsApiFactory CreateFactory() => new(_connectionString,
        $"localhost:{_minio.GetMappedPublicPort(9000)}");

    private static async Task<JsonElement> StartInspectionAsync(HttpClient inspector)
    {
        var queue = await inspector.GetFromJsonAsync<JsonElement>("/api/inspections/queue");
        var vehicleId = queue[0].GetProperty("id").GetGuid();
        using var started = await inspector.PostAsJsonAsync($"/api/vehicles/{vehicleId}/inspections/start",
            new { mileageKm = 31_600 });
        return await ReadJsonAsync(started);
    }

    private static async Task<JsonElement> AddMinorDefectAsync(HttpClient inspector, Guid inspectionId,
        Guid defectId, long expectedVersion, string title)
    {
        using var response = await inspector.PostAsJsonAsync($"/api/inspections/{inspectionId}/defects", new
        {
            defectId,
            category = "Body",
            title,
            description = "РџСЂРѕРІРµСЂРѕС‡РЅС‹Р№ РґРµС„РµРєС‚",
            severity = "Minor",
            repairRequired = false,
            blocksPublication = false,
            blocksTestDrive = false,
            blocksSale = false,
            expectedVersion
        });
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> CompleteRequiredItemsAsync(HttpClient inspector, Guid inspectionId,
        JsonElement detail)
    {
        foreach (var item in detail.GetProperty("items").EnumerateArray())
        {
            using var response = await inspector.PutAsJsonAsync(
                $"/api/inspections/{inspectionId}/items/{item.GetProperty("id").GetGuid()}",
                new { result = "Pass", expectedVersion = detail.GetProperty("version").GetInt64() });
            detail = await ReadJsonAsync(response);
        }
        return detail;
    }

    private async Task MigrateToPreviousReleaseAsync()
    {
        var options = new DbContextOptionsBuilder<DealerOsDbContext>().UseNpgsql(_connectionString).Options;
        await using var db = new DealerOsDbContext(options);
        await db.Database.MigrateAsync("20260712181329_TenantIntegrityAndValidation");
    }

    private static async Task AuthenticateAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "DealerOS!2026" });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            body.GetProperty("accessToken").GetString());
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid inspectionId, Guid defectId,
        Guid photoId, long expectedVersion, byte[] bytes, string fileName, string contentType)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(photoId.ToString()), "photoId");
        form.Add(new StringContent(expectedVersion.ToString()), "expectedVersion");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return await client.PostAsync($"/api/inspections/{inspectionId}/defects/{defectId}/photos", form);
    }

    private static byte[] ValidPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
