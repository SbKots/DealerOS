using System.Text.Json;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Vehicles.Application;

public sealed class VehicleIntakeService(IVehicleStore store, IAuditWriter auditWriter, TimeProvider timeProvider)
{
    public async Task<VehicleResponse> CreateAsync(ActorContext actor, CreateVehicleRequest request, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesCreate);
        DemandBranch(actor, request.BranchId);

        var branchCode = await store.GetBranchCodeAsync(actor.OrganizationId, request.BranchId, cancellationToken)
            ?? throw new ForbiddenException("Филиал не принадлежит текущей организации или недоступен пользователю.");
        var vin = new Vin(request.Vin);
        if (await store.VinExistsAsync(actor.OrganizationId, vin.Value, cancellationToken))
        {
            throw new ConflictException("vehicle.duplicate_vin", "Автомобиль с таким VIN уже существует в организации.");
        }

        var now = timeProvider.GetUtcNow();
        var vehicle = Vehicle.CreateDraft(actor.OrganizationId, request.BranchId, vin.Value, request.Make, request.Model,
            request.Year, request.MileageKm, new Money(request.PlannedPurchaseAmount, request.Currency), now, actor.UserId);
        await store.AddAsync(vehicle, cancellationToken);
        auditWriter.Write(actor.OrganizationId, actor.UserId, "vehicle.intake_created", "Vehicle", vehicle.Id, null,
            JsonSerializer.Serialize(new { vehicle.Vin, vehicle.Status, vehicle.BranchId, vehicle.PlannedPurchaseAmount, vehicle.Currency }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await store.GetAsync(actor.OrganizationId, vehicle.Id, actor.BranchIds, cancellationToken)
            ?? throw new InvalidOperationException($"Vehicle {vehicle.Id} was created but cannot be read.");
    }

    public async Task<VehicleResponse> AcceptAsync(ActorContext actor, Guid vehicleId, string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesAccept);
        var vehicle = await store.FindAsync(actor.OrganizationId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        DemandBranch(actor, vehicle.BranchId);
        var branchCode = await store.GetBranchCodeAsync(actor.OrganizationId, vehicle.BranchId, cancellationToken)
            ?? throw new ForbiddenException("Филиал автомобиля недоступен.");

        var oldValue = JsonSerializer.Serialize(new { vehicle.Status, vehicle.StockNumber });
        var now = timeProvider.GetUtcNow();
        vehicle.AcceptToStock(branchCode, now, actor.UserId);
        auditWriter.Write(actor.OrganizationId, actor.UserId, "vehicle.accepted_to_stock", "Vehicle", vehicle.Id,
            oldValue, JsonSerializer.Serialize(new { vehicle.Status, vehicle.StockNumber, vehicle.AcceptedAt }), correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await store.GetAsync(actor.OrganizationId, vehicle.Id, actor.BranchIds, cancellationToken)
            ?? throw new InvalidOperationException($"Vehicle {vehicle.Id} was accepted but cannot be read.");
    }

    public async Task<IReadOnlyList<VehicleResponse>> ListAsync(ActorContext actor, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesRead);
        return await store.ListAsync(actor.OrganizationId, actor.BranchIds, cancellationToken);
    }

    public async Task<VehicleResponse> GetAsync(ActorContext actor, Guid vehicleId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.VehiclesRead);
        return await store.GetAsync(actor.OrganizationId, vehicleId, actor.BranchIds, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
    }

    private static void Demand(ActorContext actor, string permission)
    {
        if (!actor.Permissions.Contains(permission)) throw new ForbiddenException("Недостаточно прав для операции.");
    }

    private static void DemandBranch(ActorContext actor, Guid branchId)
    {
        if (!actor.BranchIds.Contains(branchId)) throw new ForbiddenException("Филиал недоступен пользователю.");
    }
}
