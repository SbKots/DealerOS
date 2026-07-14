using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DealerOS.Api.Infrastructure;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace DealerOS.IntegrationTests;

[Collection(ReconditioningPostgresCollection.Name)]
public sealed class VehiclePhotoGalleryApiTests(ReconditioningPostgresFixture database)
{
    [Fact]
    public async Task ConcurrentGalleryUpload_IsIdempotentAndNeverDeletesTheWinningObjects()
    {
        await using var lease = await database.BeginTestAsync();
        await using var factory = new DealerOsApiFactory(database.ConnectionString, database.ObjectStorageEndpoint);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        await AuthenticateAsync(firstClient, "admin@volga-auto.demo");
        await AuthenticateAsync(secondClient, "admin@volga-auto.demo");
        var vehicles = await firstClient.GetFromJsonAsync<JsonElement>("/api/vehicles");
        var vehicleId = vehicles[0].GetProperty("id").GetGuid();
        var otherVehicleId = vehicles.EnumerateArray().First(x => x.GetProperty("id").GetGuid() != vehicleId)
            .GetProperty("id").GetGuid();
        var mediaId = Guid.NewGuid();
        var bytes = CreateImage(1280, 720);

        var responses = await Task.WhenAll(
            UploadAsync(firstClient, vehicleId, mediaId, bytes, "concurrent-a.jpg", "image/jpeg", "Front"),
            UploadAsync(secondClient, vehicleId, mediaId, bytes, "concurrent-b.jpg", "image/jpeg", "Rear"));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.Equal(1, await factory.QueryDbAsync(db => db.VehicleMedia.CountAsync(x => x.Id == mediaId)));
            var registered = await ReadJsonAsync(responses[0]);
            using var download = await firstClient.GetAsync(registered.GetProperty("largeUrl").GetString());
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.NotEmpty(await download.Content.ReadAsByteArrayAsync());

            using var retry = await UploadAsync(firstClient, vehicleId, mediaId, bytes,
                "retry.jpg", "image/jpeg", "Other");
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            using var wrongVehicle = await UploadAsync(firstClient, otherVehicleId, mediaId, bytes,
                "wrong.jpg", "image/jpeg", "Other");
            Assert.Equal(HttpStatusCode.Conflict, wrongVehicle.StatusCode);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Gallery_RejectsVehicleFromAnUnassignedBranch()
    {
        await using var lease = await database.BeginTestAsync();
        await using var factory = new DealerOsApiFactory(database.ConnectionString, database.ObjectStorageEndpoint);
        var vehicleId = await factory.ExecuteDbWithResultAsync(async db =>
        {
            var vehicle = Vehicle.CreateDraft(DemoSeed.VolgaOrganizationId, DemoSeed.VolgaSecondaryBranchId,
                $"BRANCH{Guid.NewGuid():N}"[..17].ToUpperInvariant(), "Branch", "Hidden", 2024, 10,
                new Money(1_000_000m, "RUB"), DateTimeOffset.UtcNow, DemoSeed.VolgaAdminUserId);
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            return vehicle.Id;
        });
        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin, "admin@volga-auto.demo");

        using var response = await admin.GetAsync($"/api/vehicles/{vehicleId}/photos");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Gallery_UploadsVariantsManagesCoverOrderAndDeletionWithTenantAndPermissionIsolation()
    {
        await using var lease = await database.BeginTestAsync();
        await using var factory = new DealerOsApiFactory(database.ConnectionString, database.ObjectStorageEndpoint);
        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin, "admin@volga-auto.demo");
        var vehicleId = (await admin.GetFromJsonAsync<JsonElement>("/api/vehicles"))[0].GetProperty("id").GetGuid();

        using var spoofed = await UploadAsync(admin, vehicleId, Guid.NewGuid(), Encoding.UTF8.GetBytes("not an image"),
            "fake.jpg", "image/jpeg", "Front");
        Assert.Equal(HttpStatusCode.BadRequest, spoofed.StatusCode);
        using var oversized = await UploadAsync(admin, vehicleId, Guid.NewGuid(),
            new byte[InspectionImageProcessor.MaxInputBytes + 1], "large.png", "image/png", null);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        using var unsupported = await UploadAsync(admin, vehicleId, Guid.NewGuid(), CreateGif(),
            "camera.gif", "image/gif", null);
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);

        var firstId = Guid.NewGuid();
        using var firstUpload = await UploadAsync(admin, vehicleId, firstId, CreateImage(1600, 900),
            "front.jpg", "image/jpeg", "Front");
        Assert.Equal(HttpStatusCode.OK, firstUpload.StatusCode);
        var first = await ReadJsonAsync(firstUpload);
        Assert.Equal(1600, first.GetProperty("width").GetInt32());
        Assert.Equal(900, first.GetProperty("height").GetInt32());
        Assert.Equal("Front", first.GetProperty("category").GetString());
        var firstStored = await factory.QueryDbAsync(db => db.VehicleMedia.Where(x => x.Id == firstId)
            .Select(x => new
            {
                x.ThumbnailWidth,
                x.ThumbnailHeight,
                x.MediumWidth,
                x.MediumHeight,
                x.LargeWidth,
                x.LargeHeight
            }).SingleAsync());
        Assert.Equal((480, 270), (firstStored.ThumbnailWidth, firstStored.ThumbnailHeight));
        Assert.Equal((1280, 720), (firstStored.MediumWidth, firstStored.MediumHeight));
        Assert.Equal((1600, 900), (firstStored.LargeWidth, firstStored.LargeHeight));

        foreach (var property in new[] { "thumbnailUrl", "mediumUrl", "largeUrl", "originalUrl" })
        {
            using var download = await admin.GetAsync(first.GetProperty(property).GetString());
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("image/jpeg", download.Content.Headers.ContentType?.MediaType);
            Assert.NotEmpty(await download.Content.ReadAsByteArrayAsync());
        }

        var secondId = Guid.NewGuid();
        using var secondUpload = await UploadAsync(admin, vehicleId, secondId, CreateImage(300, 1200),
            "vertical.jpg", "image/jpeg", null);
        var second = await ReadJsonAsync(secondUpload);
        Assert.Equal(300, second.GetProperty("width").GetInt32());
        Assert.Equal(1200, second.GetProperty("height").GetInt32());

        first = await PostAndReadAsync(admin, $"/api/vehicles/{vehicleId}/photos/{firstId}/cover",
            new { expectedVersion = first.GetProperty("version").GetInt64() });
        Assert.True(first.GetProperty("isCover").GetBoolean());
        first = await PutAndReadAsync(admin, $"/api/vehicles/{vehicleId}/photos/{firstId}", new
        {
            category = "MainView",
            caption = "Главный вид после подготовки",
            focalPointX = 0.25m,
            focalPointY = 0.5m,
            expectedVersion = first.GetProperty("version").GetInt64()
        });
        Assert.Equal("Главный вид после подготовки", first.GetProperty("caption").GetString());
        using (var staleMetadata = await admin.PutAsJsonAsync($"/api/vehicles/{vehicleId}/photos/{firstId}", new
        {
            category = "MainView",
            caption = "Конкурентная запись",
            focalPointX = 0.5m,
            focalPointY = 0.5m,
            expectedVersion = first.GetProperty("version").GetInt64() - 1
        }))
            Assert.Equal(HttpStatusCode.Conflict, staleMetadata.StatusCode);

        var reordered = await PostAndReadAsync(admin, $"/api/vehicles/{vehicleId}/photos/reorder", new
        {
            items = new[]
            {
                new { mediaId = secondId, sortOrder = 10, expectedVersion = second.GetProperty("version").GetInt64() },
                new { mediaId = firstId, sortOrder = 20, expectedVersion = first.GetProperty("version").GetInt64() }
            }
        });
        Assert.Equal(secondId, reordered[0].GetProperty("id").GetGuid());

        var vehicles = await admin.GetFromJsonAsync<JsonElement>("/api/vehicles");
        Assert.Equal(firstId, vehicles.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == vehicleId)
            .GetProperty("coverPhotoId").GetGuid());

        await factory.SetUserAsync(DemoSeed.VolgaViewerUserId, true,
            Permissions.VehiclesRead, Permissions.VehiclesPhotosView);
        using var viewer = factory.CreateClient();
        await AuthenticateAsync(viewer, "viewer@volga-auto.demo");
        Assert.Equal(HttpStatusCode.OK,
            (await viewer.GetAsync($"/api/vehicles/{vehicleId}/photos")).StatusCode);
        using (var forbiddenUpload = await UploadAsync(viewer, vehicleId, Guid.NewGuid(), CreateImage(20, 20),
                   "forbidden.jpg", "image/jpeg", "Front"))
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenUpload.StatusCode);
        using (var forbiddenManage = await viewer.PostAsJsonAsync(
                   $"/api/vehicles/{vehicleId}/photos/{firstId}/cover",
                   new { expectedVersion = first.GetProperty("version").GetInt64() }))
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenManage.StatusCode);
        using var north = factory.CreateClient();
        await AuthenticateAsync(north, "admin@north-auto.demo");
        Assert.Equal(HttpStatusCode.NotFound,
            (await north.GetAsync($"/api/vehicles/{vehicleId}/photos")).StatusCode);

        using var delete = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/vehicles/{vehicleId}/photos/{secondId}?expectedVersion={reordered[0].GetProperty("version").GetInt64()}");
        using var deleted = await admin.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(1, await factory.QueryDbAsync(db => db.VehicleMedia.CountAsync(x => x.VehicleId == vehicleId)));
        Assert.True(await factory.QueryDbAsync(db => db.AuditEvents.CountAsync(x =>
            x.OrganizationId == DemoSeed.VolgaOrganizationId && x.Operation.StartsWith("vehicle.photo_"))) >= 5);
    }

    [Fact]
    public async Task Gallery_AppliesExifOrientationAndDoesNotUpscaleSmallImages()
    {
        await using var lease = await database.BeginTestAsync();
        await using var factory = new DealerOsApiFactory(database.ConnectionString, database.ObjectStorageEndpoint);
        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin, "admin@volga-auto.demo");
        var vehicleId = (await admin.GetFromJsonAsync<JsonElement>("/api/vehicles"))[0].GetProperty("id").GetGuid();

        using var upload = await UploadAsync(admin, vehicleId, Guid.NewGuid(), CreateExifOrientedJpeg(40, 20, 6),
            "phone.jpg", "image/jpeg", "Interior");
        var media = await ReadJsonAsync(upload);
        Assert.Equal(20, media.GetProperty("width").GetInt32());
        Assert.Equal(40, media.GetProperty("height").GetInt32());

        var stored = await factory.QueryDbAsync(db => db.VehicleMedia.SingleAsync(x =>
            x.Id == media.GetProperty("id").GetGuid()));
        Assert.Equal(20, stored.ThumbnailWidth);
        Assert.Equal(40, stored.ThumbnailHeight);
        Assert.Equal(20, stored.LargeWidth);
        Assert.Equal(40, stored.LargeHeight);
        using var normalizedDownload = await admin.GetAsync(media.GetProperty("originalUrl").GetString());
        var normalizedBytes = await normalizedDownload.Content.ReadAsByteArrayAsync();
        using var normalizedCodec = SKCodec.Create(new MemoryStream(normalizedBytes));
        Assert.NotNull(normalizedCodec);
        Assert.Equal(SKEncodedOrigin.TopLeft, normalizedCodec.EncodedOrigin);
    }

    [Fact]
    public async Task CompletedInspectionPhoto_IsSharedWithGalleryAndSurvivesGalleryDeletion()
    {
        await using var lease = await database.BeginTestAsync();
        await using var factory = new DealerOsApiFactory(database.ConnectionString, database.ObjectStorageEndpoint);
        using var inspector = factory.CreateClient();
        await AuthenticateAsync(inspector, "inspector@volga-auto.demo");
        var queue = await inspector.GetFromJsonAsync<JsonElement>("/api/inspections/queue");
        var vehicleId = queue[0].GetProperty("id").GetGuid();
        var inspection = await PostAndReadAsync(inspector, $"/api/vehicles/{vehicleId}/inspections/start",
            new { mileageKm = 31_600 });
        var inspectionId = inspection.GetProperty("id").GetGuid();
        foreach (var item in inspection.GetProperty("items").EnumerateArray())
        {
            inspection = await PutAndReadAsync(inspector,
                $"/api/inspections/{inspectionId}/items/{item.GetProperty("id").GetGuid()}", new
                {
                    result = "Pass",
                    comment = "Проверено",
                    expectedVersion = inspection.GetProperty("version").GetInt64()
                });
        }
        var defectId = Guid.NewGuid();
        inspection = await PostAndReadAsync(inspector, $"/api/inspections/{inspectionId}/defects", new
        {
            defectId,
            category = "Body",
            title = "Скол лакокрасочного покрытия",
            description = "Фотофиксация",
            severity = "Minor",
            repairRequired = false,
            blocksPublication = false,
            blocksTestDrive = false,
            blocksSale = false,
            expectedVersion = inspection.GetProperty("version").GetInt64()
        });
        var photoId = Guid.NewGuid();
        using (var photoUpload = await UploadInspectionAsync(inspector, inspectionId, defectId, photoId,
                   inspection.GetProperty("version").GetInt64(), CreateImage(900, 1200)))
            inspection = await ReadJsonAsync(photoUpload);
        inspection = await PostAndReadAsync(inspector, $"/api/inspections/{inspectionId}/complete", new
        {
            finalComment = "Осмотр завершён",
            expectedVersion = inspection.GetProperty("version").GetInt64()
        });
        Assert.Equal("Completed", inspection.GetProperty("status").GetString());

        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin, "admin@volga-auto.demo");
        var sources = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/vehicles/{vehicleId}/photos/inspection-sources");
        Assert.Contains(sources.EnumerateArray(), x => x.GetProperty("photoId").GetGuid() == photoId);
        var mediaId = Guid.NewGuid();
        var imported = await PostAndReadAsync(admin, $"/api/vehicles/{vehicleId}/photos/import-inspection", new
        {
            mediaId,
            inspectionPhotoId = photoId,
            category = "Defect"
        });
        Assert.Equal(photoId, imported.GetProperty("sourceInspectionPhotoId").GetGuid());
        var gallery = await factory.QueryDbAsync(db => db.VehicleMedia.Where(x => x.Id == mediaId)
            .Select(x => new { x.ObjectKey, x.OwnsOriginalObject }).SingleAsync());
        var inspectionObjectKey = await factory.QueryDbAsync(db => db.InspectionPhotos.Where(x => x.Id == photoId)
            .Select(x => x.ObjectKey).SingleAsync());
        Assert.Equal(inspectionObjectKey, gallery.ObjectKey);
        Assert.False(gallery.OwnsOriginalObject);

        using var delete = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/vehicles/{vehicleId}/photos/{mediaId}?expectedVersion={imported.GetProperty("version").GetInt64()}");
        using var deleted = await admin.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var evidence = await inspector.GetAsync(
            $"/api/inspections/{inspectionId}/defects/{defectId}/photos/{photoId}");
        Assert.Equal(HttpStatusCode.OK, evidence.StatusCode);
        Assert.NotEmpty(await evidence.Content.ReadAsByteArrayAsync());
    }

    private static async Task AuthenticateAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "DealerOS!2026" });
        var session = await ReadJsonAsync(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            session.GetProperty("accessToken").GetString());
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid vehicleId, Guid mediaId,
        byte[] bytes, string fileName, string contentType, string? category)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(mediaId.ToString()), "mediaId");
        if (category is not null) form.Add(new StringContent(category), "category");
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return await client.PostAsync($"/api/vehicles/{vehicleId}/photos", form);
    }

    private static async Task<HttpResponseMessage> UploadInspectionAsync(HttpClient client, Guid inspectionId,
        Guid defectId, Guid photoId, long expectedVersion, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(photoId.ToString()), "photoId");
        form.Add(new StringContent(expectedVersion.ToString()), "expectedVersion");
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "inspection.jpg");
        return await client.PostAsync($"/api/inspections/{inspectionId}/defects/{defectId}/photos", form);
    }

    private static async Task<JsonElement> PostAndReadAsync(HttpClient client, string path, object? body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> PutAndReadAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PutAsJsonAsync(path, body);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {json}");
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static byte[] CreateImage(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(20, 83, 61));
        using var paint = new SKPaint { Color = new SKColor(139, 197, 32), IsAntialias = true };
        canvas.DrawCircle(width * 0.5f, height * 0.5f, Math.Min(width, height) * 0.25f, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 88);
        return data.ToArray();
    }

    private static byte[] CreateGif()
    {
        // Valid minimal 1x1 GIF, intentionally unsupported by the gallery policy.
        return Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
    }

    private static byte[] CreateExifOrientedJpeg(int width, int height, ushort orientation)
    {
        var jpeg = CreateImage(width, height);
        var exif = new byte[]
        {
            0xFF, 0xE1, 0x00, 0x22, 0x45, 0x78, 0x69, 0x66, 0x00, 0x00,
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
            (byte)orientation, (byte)(orientation >> 8), 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };
        return [jpeg[0], jpeg[1], .. exif, .. jpeg[2..]];
    }
}
