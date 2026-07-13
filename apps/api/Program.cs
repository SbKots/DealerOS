using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DealerOS.Api.Auth;
using DealerOS.Api.Endpoints;
using DealerOS.Api.Infrastructure;
using DealerOS.Modules.Crm.Application;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Inspections.Application;
using DealerOS.Modules.Operations.Application;
using DealerOS.Modules.Reconditioning.Application;
using DealerOS.Modules.Vehicles.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Minio;

var builder = WebApplication.CreateBuilder(args);

var configuredJwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
if (builder.Environment.IsProduction() && configuredJwtKey.StartsWith("local-", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Production requires a non-local Jwt:Key from a secret provider.");
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddOpenApiDocument(options =>
{
    options.Title = "DealerOS API";
    options.Version = "v1";
});
builder.Services.AddHealthChecks().AddDbContextCheck<DealerOsDbContext>(tags: ["ready"])
    .AddCheck<ObjectStorageHealthCheck>("object_storage", tags: ["ready"]);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:5173", "http://localhost:4173")
    .AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddDbContext<DealerOsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DealerOS")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<VehicleStore>();
builder.Services.AddScoped<IVehicleStore>(sp => sp.GetRequiredService<VehicleStore>());
builder.Services.AddScoped<IAuditWriter>(sp => sp.GetRequiredService<VehicleStore>());
builder.Services.AddScoped<VehicleIntakeService>();
builder.Services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<InspectionStore>();
builder.Services.AddScoped<IInspectionStore>(sp => sp.GetRequiredService<InspectionStore>());
builder.Services.AddScoped<IInspectionPhotoStorage, MinioInspectionPhotoStorage>();
builder.Services.AddScoped<IInspectionImageProcessor, InspectionImageProcessor>();
builder.Services.AddScoped<InspectionService>();
builder.Services.AddScoped<ReconditioningStore>();
builder.Services.AddScoped<IReconditioningStore>(sp => sp.GetRequiredService<ReconditioningStore>());
builder.Services.AddScoped<ReconditioningService>();
builder.Services.AddScoped<OperationsStore>();
builder.Services.AddScoped<IOperationsStore>(sp => sp.GetRequiredService<OperationsStore>());
builder.Services.AddScoped<OperationsService>();
builder.Services.AddScoped<QualityListingStore>();
builder.Services.AddScoped<IQualityListingStore>(sp => sp.GetRequiredService<QualityListingStore>());
builder.Services.AddScoped<QualityControlService>();
builder.Services.AddScoped<MediaListingService>();
builder.Services.AddScoped<CrmStore>();
builder.Services.AddScoped<ICrmStore>(sp => sp.GetRequiredService<CrmStore>());
builder.Services.AddScoped<CrmService>();
builder.Services.AddHostedService<OperationsDeadlineWorker>();
builder.Services.AddSingleton<IMinioClient>(_ =>
{
    var endpoint = builder.Configuration["ObjectStorage:Endpoint"]
        ?? throw new InvalidOperationException("ObjectStorage:Endpoint is not configured.");
    var accessKey = builder.Configuration["ObjectStorage:AccessKey"]
        ?? throw new InvalidOperationException("ObjectStorage:AccessKey is not configured.");
    var secretKey = builder.Configuration["ObjectStorage:SecretKey"]
        ?? throw new InvalidOperationException("ObjectStorage:SecretKey is not configured.");
    return new MinioClient().WithEndpoint(endpoint).WithCredentials(accessKey, secretKey)
        .WithSSL(builder.Configuration.GetValue("ObjectStorage:UseSsl", false)).Build();
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IConfiguration>((options, configuration) =>
{
    var jwtKey = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = configuration["Jwt:Issuer"],
        ValidAudience = configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        NameClaimType = "name"
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirst("user_id")?.Value, out var userId)
                || !Guid.TryParse(context.Principal?.FindFirst("org_id")?.Value, out var organizationId))
            {
                context.Fail("Required session claims are missing.");
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<DealerOsDbContext>();
            var user = await db.Users.AsNoTracking().Include(x => x.BranchAccess)
                .SingleOrDefaultAsync(x => x.Id == userId && x.OrganizationId == organizationId, context.HttpContext.RequestAborted);
            if (user is null || !user.IsActive)
            {
                context.Fail("The user session has been revoked.");
                return;
            }

            var tokenBranches = context.Principal!.FindAll("branch_id").Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
            var currentBranches = user.BranchAccess.Select(x => x.BranchId.ToString()).ToHashSet(StringComparer.Ordinal);
            var tokenPermissions = context.Principal.FindAll("permission").Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
            var currentPermissions = user.PermissionSet.ToHashSet(StringComparer.Ordinal);
            if (!tokenBranches.SetEquals(currentBranches) || !tokenPermissions.SetEquals(currentPermissions))
            {
                context.Fail("The user access scope has changed; a new session is required.");
            }
        }
    };
});
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.VehicleOperator)
    {
        options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
    }
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    var supplied = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    context.TraceIdentifier = !string.IsNullOrWhiteSpace(supplied) && supplied.Length <= 100 ? supplied : Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    await next();
});
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Configuration.GetValue("Database:ApplyMigrations", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<DealerOsDbContext>();
    await db.Database.MigrateAsync();
    if (app.Configuration.GetValue("Database:SeedDemo", false))
    {
        await DemoSeed.ApplyAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserAccount>>(), CancellationToken.None);
    }
}

if (app.Configuration.GetValue("ObjectStorage:EnsureBucket", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IInspectionPhotoStorage>().EnsureBucketAsync(CancellationToken.None);
}

if (app.Environment.IsDevelopment()) app.UseOpenApi();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapAuthEndpoints();
app.MapOrganizationEndpoints();
app.MapVehicleEndpoints();
app.MapInspectionEndpoints();
app.MapReconditioningEndpoints();
app.MapOperationsEndpoints();
app.MapQualityListingEndpoints();
app.MapCrmEndpoints();
app.Run();

public partial class Program;
