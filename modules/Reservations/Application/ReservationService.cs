using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Reservations.Domain;
using DealerOS.Modules.Sales.Domain;
using DealerOS.Modules.Vehicles.Application;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Reservations.Application;

public sealed class ReservationService(IReservationStore store, IAuditWriter audit, TimeProvider timeProvider)
{
    public async Task<ReservationResponse> CreateAsync(ActorContext actor, CreateReservationRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReservationsCreate);
        var existing = await FindExistingCreateAsync(actor.OrganizationId, actor.UserId, request, cancellationToken);
        if (existing is not null) return await MapAsync(actor, existing, cancellationToken);

        var source = await store.FindSourceAsync(actor.OrganizationId, request.ApprovedOfferSnapshotId,
            cancellationToken) ?? throw new NotFoundException("Утверждённое предложение не найдено.");
        DemandBranch(actor, source.Offer.BranchId);
        var now = timeProvider.GetUtcNow();
        EnsureSourceAvailable(source, request, now);
        if (await store.HasNewerApprovedOfferAsync(actor.OrganizationId, source.Offer.LeadId,
                source.Offer.Revision, cancellationToken))
            throw new ConflictException("reservation.offer_superseded",
                "Для лида уже существует более новая утверждённая ревизия предложения.");

        var reservation = Reservation.Create(request.ReservationId, actor.OrganizationId, source.Offer.BranchId,
            source.Offer.VehicleId, source.Offer.CustomerId, source.Offer.LeadId, source.Snapshot.Id,
            actor.UserId, request.CommandId, request.ExpiresAt, request.DepositRequired, request.DepositAmount,
            request.Currency ?? string.Empty, now);
        source.Vehicle.Reserve(now, actor.UserId);
        await store.AddAsync(reservation, cancellationToken);
        WriteAudit(actor.OrganizationId, actor.UserId, "reservation.created", reservation, correlationId, now);
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            store.ResetTracking();
            existing = await FindExistingCreateAsync(actor.OrganizationId, actor.UserId, request, cancellationToken);
            if (existing is not null) return await MapAsync(actor, existing, cancellationToken);
            throw new ConflictException("reservation.vehicle_already_reserved",
                "Автомобиль уже забронирован или изменён конкурентно.");
        }
        return await MapAsync(actor, reservation, cancellationToken);
    }

    public async Task<IReadOnlyList<ReservationResponse>> ListAsync(ActorContext actor,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.ReservationsView);
        var result = new List<ReservationResponse>();
        foreach (var reservation in await store.ListAsync(actor.OrganizationId, actor.BranchIds, cancellationToken))
            result.Add(await MapAsync(actor, reservation, cancellationToken));
        return result;
    }

    public async Task<ReservationResponse> GetAsync(ActorContext actor, Guid reservationId,
        CancellationToken cancellationToken) => await MapAsync(actor,
        await FindAsync(actor, reservationId, Permissions.ReservationsView, cancellationToken), cancellationToken);

    public async Task<ReservationResponse> ExtendAsync(ActorContext actor, Guid reservationId,
        ExtendReservationRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var reservation = await FindAsync(actor, reservationId, Permissions.ReservationsExtend, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var offerValidUntil = await store.FindSnapshotValidUntilAsync(actor.OrganizationId,
            reservation.ApprovedOfferSnapshotId, cancellationToken)
            ?? throw new NotFoundException("Approved Offer snapshot брони не найден.");
        if (request.ExpiresAt > offerValidUntil)
            throw new DomainException("reservation.extension_exceeds_offer",
                "Срок брони не может превышать срок Approved Offer.");
        if (!reservation.Extend(request.CommandId, request.ExpiresAt, request.Reason, request.ExpectedVersion,
                actor.UserId, now)) return await MapAsync(actor, reservation, cancellationToken);
        WriteAudit(actor.OrganizationId, actor.UserId, "reservation.extended", reservation, correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await MapAsync(actor, reservation, cancellationToken);
    }

    public async Task<ReservationResponse> RegisterDepositAsync(ActorContext actor, Guid reservationId,
        RegisterReservationDepositRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var reservation = await FindAsync(actor, reservationId, Permissions.ReservationsDeposit,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!reservation.RegisterDeposit(request.CommandId, request.Status, request.ManualReference,
                request.Reason, request.ExpectedVersion, actor.UserId, now))
            return await MapAsync(actor, reservation, cancellationToken);
        if (reservation.Status == ReservationStatus.DepositFailed)
            await ReleaseVehicleAsync(reservation, actor.UserId, now, cancellationToken);
        WriteAudit(actor.OrganizationId, actor.UserId, "reservation.deposit_registered", reservation,
            correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await MapAsync(actor, reservation, cancellationToken);
    }

    public async Task<ReservationResponse> CancelAsync(ActorContext actor, Guid reservationId,
        ReservationReasonCommandRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var reservation = await FindAsync(actor, reservationId, Permissions.ReservationsCancel, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!reservation.Cancel(request.CommandId, request.Reason, request.ExpectedVersion, actor.UserId, now))
            return await MapAsync(actor, reservation, cancellationToken);
        await ReleaseVehicleAsync(reservation, actor.UserId, now, cancellationToken);
        WriteAudit(actor.OrganizationId, actor.UserId, "reservation.cancelled", reservation, correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await MapAsync(actor, reservation, cancellationToken);
    }

    public async Task<ReservationResponse> ExpireAsync(ActorContext actor, Guid reservationId,
        ReservationCommandRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var reservation = await FindAsync(actor, reservationId, Permissions.ReservationsEdit, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!reservation.Expire(request.CommandId, request.ExpectedVersion, actor.UserId, now))
            return await MapAsync(actor, reservation, cancellationToken);
        await ReleaseVehicleAsync(reservation, actor.UserId, now, cancellationToken);
        WriteAudit(actor.OrganizationId, actor.UserId, "reservation.expired", reservation, correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return await MapAsync(actor, reservation, cancellationToken);
    }

    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expired = 0;
        foreach (var reservation in await store.ListDueAsync(now, cancellationToken))
        {
            try
            {
                if (!reservation.Expire(BuildExpirationCommandId(reservation.Id), reservation.Version,
                        reservation.CreatedByUserId, now)) continue;
                await ReleaseVehicleAsync(reservation, reservation.CreatedByUserId, now, cancellationToken);
                WriteAudit(reservation.OrganizationId, reservation.CreatedByUserId, "reservation.auto_expired",
                    reservation, "reservation-expiration-worker", now);
                await store.SaveChangesAsync(cancellationToken);
                expired++;
            }
            catch (ConflictException)
            {
                store.ResetTracking();
            }
        }
        return expired;
    }

    private async Task<Reservation?> FindExistingCreateAsync(Guid organizationId, Guid actorUserId,
        CreateReservationRequest request, CancellationToken cancellationToken)
    {
        var byId = await store.FindAsync(organizationId, request.ReservationId, cancellationToken);
        var byCommand = await store.FindByCreateCommandAsync(organizationId, request.CommandId, cancellationToken);
        if (byId is not null && byCommand is not null && byId.Id != byCommand.Id)
            throw new ConflictException("reservation.command_conflict", "Command ID уже относится к другой брони.");
        var existing = byId ?? byCommand;
        if (existing is null) return null;
        if (existing.CreatedByUserId != actorUserId || !existing.MatchesCreate(request.ApprovedOfferSnapshotId,
                request.ExpiresAt, request.DepositRequired, request.DepositAmount, request.Currency ?? string.Empty))
            throw new ConflictException("reservation.id_conflict", "Идентификатор брони или команды использован с другим payload.");
        return existing;
    }

    private static void EnsureSourceAvailable(ReservationSource source, CreateReservationRequest request,
        DateTimeOffset now)
    {
        if (source.Offer.Status != SalesOfferStatus.Approved || source.Offer.ApprovedSnapshot?.Id != source.Snapshot.Id)
            throw new ConflictException("reservation.offer_not_approved", "Бронь требует актуальный Approved Offer snapshot.");
        if (source.Snapshot.ValidUntil < now)
            throw new ConflictException("reservation.offer_expired", "Срок утверждённого предложения истёк.");
        if (request.ExpiresAt > source.Snapshot.ValidUntil)
            throw new DomainException("reservation.expiration_exceeds_offer",
                "Срок брони не может превышать срок Approved Offer.");
        if (source.Vehicle.BranchId != source.Offer.BranchId || source.Vehicle.Status != VehicleStatus.ReadyForSale)
            throw new ConflictException("reservation.vehicle_not_available", "Автомобиль недоступен для бронирования.");
        var currency = new Money(0, request.Currency).Currency;
        if (currency != source.Snapshot.Currency)
            throw new DomainException("reservation.currency_mismatch", "Валюта предоплаты должна совпадать с предложением.");
        if (request.DepositAmount > source.Snapshot.FinalPriceAmount)
            throw new DomainException("reservation.deposit_exceeds_offer", "Предоплата не может превышать сумму предложения.");
    }

    private async Task<Reservation> FindAsync(ActorContext actor, Guid reservationId, string permission,
        CancellationToken cancellationToken)
    {
        Demand(actor, permission);
        var reservation = await store.FindAsync(actor.OrganizationId, reservationId, cancellationToken)
            ?? throw new NotFoundException("Бронь не найдена.");
        DemandBranch(actor, reservation.BranchId);
        return reservation;
    }

    private async Task ReleaseVehicleAsync(Reservation reservation, Guid actorUserId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var vehicle = await store.FindVehicleAsync(reservation.OrganizationId, reservation.VehicleId,
            cancellationToken) ?? throw new NotFoundException("Автомобиль брони не найден.");
        vehicle.ReleaseReservation(now, actorUserId);
    }

    private async Task<ReservationResponse> MapAsync(ActorContext actor, Reservation reservation,
        CancellationToken cancellationToken)
    {
        DemandBranch(actor, reservation.BranchId);
        var vehicleName = await store.FindVehicleNameAsync(actor.OrganizationId, reservation.VehicleId,
            cancellationToken) ?? "Неизвестный автомобиль";
        var customerName = await store.FindCustomerNameAsync(actor.OrganizationId, reservation.CustomerId,
            cancellationToken) ?? "Неизвестный клиент";
        return new ReservationResponse(reservation.Id, reservation.BranchId, reservation.VehicleId, vehicleName,
            reservation.CustomerId, customerName, reservation.LeadId, reservation.ApprovedOfferSnapshotId,
            reservation.Status.ToString(), reservation.DepositStatus.ToString(), reservation.DepositAmount,
            reservation.Currency, reservation.DepositReference, reservation.CreatedAt, reservation.StartsAt,
            reservation.ExpiresAt, reservation.ClosedAt, reservation.ClosureReason, reservation.Version,
            reservation.History.OrderBy(x => x.OccurredAt).Select(x => new ReservationHistoryResponse(
                x.CommandId, x.Operation, x.OccurredAt)).ToArray());
    }

    private void WriteAudit(Guid organizationId, Guid actorUserId, string operation, Reservation reservation,
        string correlationId, DateTimeOffset now) => audit.Write(organizationId, actorUserId, operation,
        "Reservation", reservation.Id, null, JsonSerializer.Serialize(new
        {
            reservation.BranchId,
            reservation.VehicleId,
            reservation.CustomerId,
            reservation.ApprovedOfferSnapshotId,
            reservation.Status,
            reservation.DepositStatus,
            reservation.DepositAmount,
            reservation.Currency,
            reservation.ExpiresAt,
            reservation.Version
        }), correlationId, now);

    private static Guid BuildExpirationCommandId(Guid reservationId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"reservation-expire:{reservationId:N}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static void Demand(ActorContext actor, string permission)
    {
        if (!actor.Permissions.Contains(permission))
            throw new ForbiddenException("Недостаточно прав для работы с бронями.");
    }

    private static void DemandBranch(ActorContext actor, Guid branchId)
    {
        if (!actor.BranchIds.Contains(branchId))
            throw new ForbiddenException("Филиал недоступен пользователю.");
    }
}
