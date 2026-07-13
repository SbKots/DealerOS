using DealerOS.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DealerOS.IntegrationTests;

public sealed class DealerOsApiFactory(string connectionString, string? objectStorageEndpoint = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DealerOS"] = connectionString,
            ["Database:ApplyMigrations"] = "true",
            ["Database:SeedDemo"] = "true",
            ["Jwt:Issuer"] = "DealerOS.Tests",
            ["Jwt:Audience"] = "DealerOS.Tests",
            ["Jwt:Key"] = "integration-test-signing-key-with-at-least-32-characters",
            ["ObjectStorage:Endpoint"] = objectStorageEndpoint ?? "localhost:9000",
            ["ObjectStorage:AccessKey"] = "dealer-test",
            ["ObjectStorage:SecretKey"] = "dealer-test-secret",
            ["ObjectStorage:Bucket"] = "dealeros-tests",
            ["ObjectStorage:UseSsl"] = "false",
            ["ObjectStorage:EnsureBucket"] = objectStorageEndpoint is null ? "false" : "true"
        }));
    }

    public async Task<int> CountAuditEventsAsync(Guid organizationId, Guid entityId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DealerOsDbContext>();
        return db.AuditEvents.Count(x => x.OrganizationId == organizationId && x.EntityId == entityId);
    }

    public async Task<TResult> QueryDbAsync<TResult>(Func<DealerOsDbContext, Task<TResult>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<DealerOsDbContext>());
    }

    public async Task ExecuteDbAsync(Func<DealerOsDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<DealerOsDbContext>());
    }

    public async Task<TResult> ExecuteDbWithResultAsync<TResult>(Func<DealerOsDbContext, Task<TResult>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<DealerOsDbContext>());
    }

    public Task SetUserAsync(Guid userId, bool isActive, params string[] permissions) => ExecuteDbAsync(async db =>
    {
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        if (isActive) user.Activate(); else user.Deactivate();
        user.SetPermissions(permissions);
        await db.SaveChangesAsync();
    });
}
