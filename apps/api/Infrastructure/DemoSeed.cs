using DealerOS.Modules.Crm.Domain;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Inspections.Domain;
using DealerOS.Modules.Organizations;
using DealerOS.Modules.Vehicles.Domain;
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
    public static readonly Guid VolgaInspectorUserId = Guid.Parse("11111111-1111-4111-8111-111111111003");
    public static readonly Guid VolgaReconditioningUserId = Guid.Parse("11111111-1111-4111-8111-111111111004");
    public static readonly Guid VolgaManagerUserId = Guid.Parse("11111111-1111-4111-8111-111111111005");
    public static readonly Guid NorthOrganizationId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid NorthBranchId = Guid.Parse("22222222-2222-4222-8222-222222222201");
    public static readonly Guid NorthAdminUserId = Guid.Parse("22222222-2222-4222-8222-222222222001");
    public static readonly Guid VolgaTemplateId = Guid.Parse("11111111-1111-4111-8111-111111112001");
    public static readonly Guid NorthTemplateId = Guid.Parse("22222222-2222-4222-8222-222222223001");

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
        if (!await db.Users.AnyAsync(x => x.Id == VolgaInspectorUserId, cancellationToken))
            AddUser(db, hasher, VolgaInspectorUserId, VolgaOrganizationId, VolgaBranchId, "inspector@volga-auto.demo", "Дмитрий Диагност", Permissions.InspectionOperator);
        if (!await db.Users.AnyAsync(x => x.Id == VolgaReconditioningUserId, cancellationToken))
            AddUser(db, hasher, VolgaReconditioningUserId, VolgaOrganizationId, VolgaBranchId,
                "prep@volga-auto.demo", "Елена Подготовка",
                [Permissions.VehiclesRead, .. Permissions.ReconditioningOperator, .. Permissions.OperationsOperator,
                    .. Permissions.ListingOperator]);
        if (!await db.Users.AnyAsync(x => x.Id == VolgaManagerUserId, cancellationToken))
            AddUser(db, hasher, VolgaManagerUserId, VolgaOrganizationId, VolgaBranchId,
                "manager@volga-auto.demo", "Марина Руководитель",
                [Permissions.VehiclesRead, .. Permissions.ReconditioningManager, .. Permissions.OperationsManager,
                    .. Permissions.QualityManager, Permissions.ListingsView, .. Permissions.CrmManager]);
        await db.SaveChangesAsync(cancellationToken);

        await EnsurePermissionsAsync(db, VolgaAdminUserId, Permissions.VehicleOperator, cancellationToken);
        await EnsurePermissionsAsync(db, NorthAdminUserId, Permissions.VehicleOperator, cancellationToken);
        await EnsurePermissionsAsync(db, VolgaInspectorUserId, Permissions.InspectionOperator, cancellationToken);
        await EnsurePermissionsAsync(db, VolgaReconditioningUserId,
            [Permissions.VehiclesRead, .. Permissions.ReconditioningOperator, .. Permissions.OperationsOperator,
                .. Permissions.ListingOperator],
            cancellationToken);
        await EnsurePermissionsAsync(db, VolgaManagerUserId,
            [Permissions.VehiclesRead, .. Permissions.ReconditioningManager, .. Permissions.OperationsManager,
                .. Permissions.QualityManager, Permissions.ListingsView, .. Permissions.CrmManager],
            cancellationToken);

        if (!await db.InspectionTemplates.AnyAsync(x => x.Id == VolgaTemplateId, cancellationToken))
            db.InspectionTemplates.Add(CreateTemplate(VolgaTemplateId, VolgaOrganizationId, VolgaAdminUserId));
        if (!await db.InspectionTemplates.AnyAsync(x => x.Id == NorthTemplateId, cancellationToken))
            db.InspectionTemplates.Add(CreateTemplate(NorthTemplateId, NorthOrganizationId, NorthAdminUserId));
        await db.SaveChangesAsync(cancellationToken);

        const string demoInspectionVin = "XTA210990Y0200001";
        if (!await db.Vehicles.AnyAsync(x => x.OrganizationId == VolgaOrganizationId && x.Vin == demoInspectionVin,
            cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            var vehicle = Vehicle.CreateDraft(VolgaOrganizationId, VolgaBranchId, demoInspectionVin, "Lada",
                "Vesta", 2023, 31_500, new DealerOS.SharedKernel.Money(1_150_000m, "RUB"), now,
                VolgaAdminUserId);
            vehicle.AcceptToStock("MSK", now, VolgaAdminUserId);
            db.Vehicles.Add(vehicle);
        }

        const string demoReconditioningVin = "XTA210990Y0200002";
        if (!await db.Vehicles.AnyAsync(x => x.OrganizationId == VolgaOrganizationId
            && x.Vin == demoReconditioningVin, cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            var vehicle = Vehicle.CreateDraft(VolgaOrganizationId, VolgaBranchId, demoReconditioningVin,
                "Lada", "XRAY", 2022, 46_200, new DealerOS.SharedKernel.Money(980_000m, "RUB"), now,
                VolgaAdminUserId);
            vehicle.AcceptToStock("MSK", now, VolgaAdminUserId);
            vehicle.BeginInspection(now, VolgaInspectorUserId);
            var template = await db.InspectionTemplates.Include(x => x.Items).SingleAsync(
                x => x.Id == VolgaTemplateId, cancellationToken);
            var inspection = Inspection.CreateDraft(VolgaOrganizationId, VolgaBranchId, vehicle.Id,
                VolgaInspectorUserId, template, 46_200, now);
            inspection.Start(now);
            foreach (var item in inspection.Items)
                inspection.SaveItem(item.Id, InspectionItemResult.Pass, "Demo seed", inspection.Version, now);
            inspection.AddDefect(Guid.Parse("11111111-1111-4111-8111-111111113001"), InspectionCategory.Body,
                "Повреждение переднего бампера", "Трещина и нарушение креплений после осмотра.",
                DefectSeverity.Major, "Ремонт и окраска бампера", 32_000m, "RUB", true, true, false, false,
                VolgaInspectorUserId, inspection.Version, now);
            inspection.Complete("Требуется предпродажная подготовка", inspection.Version, now);
            vehicle.CompleteInspection(true, now, VolgaInspectorUserId);
            db.Vehicles.Add(vehicle);
            db.Inspections.Add(inspection);
        }
        await db.SaveChangesAsync(cancellationToken);

        const string demoCustomerEmail = "ivan.petrov@dealeros.demo";
        var demoCustomer = await db.Customers.SingleOrDefaultAsync(x => x.OrganizationId == VolgaOrganizationId
            && x.NormalizedEmail == demoCustomerEmail, cancellationToken);
        if (demoCustomer is null)
        {
            var now = DateTimeOffset.UtcNow;
            demoCustomer = Customer.Create(VolgaOrganizationId, VolgaBranchId, CustomerType.Individual,
                "Иван Петров", "+7 999 123-45-67", demoCustomerEmail, PreferredContactChannel.Phone, true,
                false, now, "Личная заявка в салоне", VolgaAdminUserId, now);
            db.Customers.Add(demoCustomer);
            await db.SaveChangesAsync(cancellationToken);
        }
        if (!await db.Leads.AnyAsync(x => x.OrganizationId == VolgaOrganizationId
            && x.CustomerId == demoCustomer.Id, cancellationToken))
        {
            var createdAt = DateTimeOffset.UtcNow.AddMinutes(-45);
            var lead = Lead.Create(VolgaOrganizationId, VolgaBranchId, demoCustomer.Id, null,
                "Кроссовер до 2 млн ₽", "Входящий звонок", VolgaAdminUserId, 30, createdAt);
            lead.Assign(Guid.NewGuid(), VolgaManagerUserId, VolgaAdminUserId, lead.Version,
                createdAt.AddMinutes(1));
            db.Leads.Add(lead);
            await db.SaveChangesAsync(cancellationToken);
        }
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

    private static async Task EnsurePermissionsAsync(DealerOsDbContext db, Guid userId, IEnumerable<string> permissions,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(x => x.Id == userId, cancellationToken);
        user.SetPermissions(permissions);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static InspectionTemplate CreateTemplate(Guid id, Guid organizationId, Guid actorUserId) => new(id,
        organizationId, "Базовый осмотр автомобиля", 1,
        [
            Item("body", InspectionCategory.Body, "Кузов", 10),
            Item("interior", InspectionCategory.Interior, "Салон", 20),
            Item("engine", InspectionCategory.Engine, "Двигатель", 30),
            Item("transmission", InspectionCategory.Transmission, "Трансмиссия", 40),
            Item("suspension", InspectionCategory.Suspension, "Подвеска", 50),
            Item("brakes", InspectionCategory.Brakes, "Тормозная система", 60),
            Item("steering", InspectionCategory.Steering, "Рулевое управление", 70),
            Item("electrical", InspectionCategory.Electrical, "Электрика", 80),
            Item("wheels", InspectionCategory.WheelsAndTires, "Колёса и шины", 90),
            Item("documents", InspectionCategory.DocumentsAndEquipment, "Документы и комплектация", 100),
            Item("test-drive", InspectionCategory.TestDrive, "Тест-драйв", 110)
        ], DateTimeOffset.UtcNow, actorUserId);

    private static InspectionTemplateItemDefinition Item(string key, InspectionCategory category, string label,
        int sortOrder) => new(key, category, label, null, true, sortOrder);
}
