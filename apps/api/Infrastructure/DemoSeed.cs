using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DealerOS.Api.Infrastructure;

public static class DemoSeed
{
    public static readonly Guid VolgaOrganizationId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid VolgaBranchId = Guid.Parse("11111111-1111-4111-8111-111111111101");
    public static readonly Guid VolgaSecondaryBranchId = Guid.Parse("11111111-1111-4111-8111-111111111102");
    public static readonly Guid VolgaAdminUserId = Guid.Parse("11111111-1111-4111-8111-111111111001");
    public static readonly Guid VolgaViewerUserId = Guid.Parse("11111111-1111-4111-8111-111111111002");
    public static readonly Guid NorthOrganizationId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid NorthBranchId = Guid.Parse("22222222-2222-4222-8222-222222222201");
    public static readonly Guid NorthAdminUserId = Guid.Parse("22222222-2222-4222-8222-222222222001");

    public static async Task ApplyAsync(DealerOsDbContext db, IPasswordHasher<UserAccount> hasher, CancellationToken cancellationToken)
    {
        if (!await db.Organizations.AnyAsync(x => x.Id == VolgaOrganizationId, cancellationToken))
            db.Organizations.Add(new Organization(VolgaOrganizationId, "Волга Авто"));
        if (!await db.Organizations.AnyAsync(x => x.Id == NorthOrganizationId, cancellationToken))
            db.Organizations.Add(new Organization(NorthOrganizationId, "Север Моторс"));

        if (!await db.Branches.AnyAsync(x => x.Id == VolgaBranchId, cancellationToken))
            db.Branches.Add(new Branch(VolgaBranchId, VolgaOrganizationId, "MSK", "Москва — основная площадка"));
        if (!await db.Branches.AnyAsync(x => x.Id == VolgaSecondaryBranchId, cancellationToken))
            db.Branches.Add(new Branch(VolgaSecondaryBranchId, VolgaOrganizationId, "KZN", "Казань — площадка"));
        if (!await db.Branches.AnyAsync(x => x.Id == NorthBranchId, cancellationToken))
            db.Branches.Add(new Branch(NorthBranchId, NorthOrganizationId, "SPB", "Санкт-Петербург — основная площадка"));

        if (!await db.Users.AnyAsync(x => x.Id == VolgaAdminUserId, cancellationToken))
            AddUser(db, hasher, VolgaAdminUserId, VolgaOrganizationId, VolgaBranchId, "admin@volga-auto.demo", "Анна Волкова", Permissions.VehicleOperator);
        if (!await db.Users.AnyAsync(x => x.Id == VolgaViewerUserId, cancellationToken))
            AddUser(db, hasher, VolgaViewerUserId, VolgaOrganizationId, VolgaBranchId, "viewer@volga-auto.demo", "Виктор Наблюдатель", [Permissions.VehiclesRead]);
        if (!await db.Users.AnyAsync(x => x.Id == NorthAdminUserId, cancellationToken))
            AddUser(db, hasher, NorthAdminUserId, NorthOrganizationId, NorthBranchId, "admin@north-auto.demo", "Николай Северин", Permissions.VehicleOperator);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddUser(DealerOsDbContext db, IPasswordHasher<UserAccount> hasher, Guid id, Guid organizationId,
        Guid branchId, string email, string name, IEnumerable<string> permissions)
    {
        var serializedPermissions = string.Join(',', permissions);
        var placeholder = new UserAccount(id, organizationId, email, name, "pending", serializedPermissions);
        var user = new UserAccount(id, organizationId, email, name, hasher.HashPassword(placeholder, "DealerOS!2026"), serializedPermissions);
        user.GrantBranch(branchId);
        db.Users.Add(user);
    }
}
