using System.Security.Cryptography;
using System.Text.Json;
using DealerOS.Modules.Deals.Domain;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Reservations.Domain;
using DealerOS.Modules.Sales.Domain;
using DealerOS.Modules.Vehicles.Application;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Deals.Application;

public sealed class DealService(IDealStore store, IDealDocumentStorage documentStorage,
    IDealPdfGenerator pdfGenerator, IAuditWriter audit, TimeProvider timeProvider)
{
    public async Task<DealResponse> CreateAsync(ActorContext actor, CreateDealRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.DealsCreate);
        var existing = await FindExistingCreateAsync(actor, request, cancellationToken);
        if (existing is not null) return Map(existing);
        var source = await store.FindSourceAsync(actor.OrganizationId, request.ReservationId, cancellationToken)
            ?? throw new NotFoundException("Активная бронь не найдена.");
        DemandBranch(actor, source.Reservation.BranchId);
        var now = timeProvider.GetUtcNow();
        EnsureSource(source, now);
        var vehicleSnapshot = JsonSerializer.Serialize(new
        {
            source.Vehicle.Id,
            source.Vehicle.Vin,
            source.Vehicle.Make,
            source.Vehicle.Model,
            source.Vehicle.Year,
            source.Vehicle.MileageKm,
            source.Vehicle.StockNumber
        });
        var deal = Deal.Create(request.DealId, actor.OrganizationId, source.Reservation.BranchId,
            source.Reservation.Id, source.Snapshot.Id, source.Reservation.CustomerId, source.Reservation.LeadId,
            source.Reservation.VehicleId, actor.UserId, request.CommandId, source.Customer.Name, vehicleSnapshot,
            source.Snapshot.LineItemsJson, source.Snapshot.BasePriceAmount, source.Snapshot.LineItemsAmount,
            source.Snapshot.DiscountAmount, source.Snapshot.FinalPriceAmount, source.Snapshot.CostSnapshotAmount,
            source.Snapshot.ExpectedMarginAmount, source.Snapshot.Currency,
            source.Reservation.DepositStatus == ReservationDepositStatus.Received,
            source.Reservation.DepositAmount, source.Reservation.DepositReference, now);
        source.Reservation.ConvertToDeal(request.CommandId, source.Reservation.Version, actor.UserId, now);
        source.Vehicle.BeginSale(now, actor.UserId);
        await store.AddAsync(deal, cancellationToken);
        WriteAudit(actor, "deal.created", deal, correlationId, now);
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            store.ResetTracking();
            existing = await FindExistingCreateAsync(actor, request, cancellationToken);
            if (existing is not null) return Map(existing);
            throw new ConflictException("deal.conversion_conflict", "Бронь уже преобразована или изменена конкурентно.");
        }
        return Map(deal);
    }

    public async Task<IReadOnlyList<DealResponse>> ListAsync(ActorContext actor,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.DealsView);
        return (await store.ListAsync(actor.OrganizationId, actor.BranchIds, cancellationToken)).Select(Map).ToArray();
    }

    public async Task<DealResponse> GetAsync(ActorContext actor, Guid dealId, CancellationToken cancellationToken) =>
        Map(await FindAsync(actor, dealId, Permissions.DealsView, cancellationToken));

    public Task<DealResponse> BeginPaymentAsync(ActorContext actor, Guid dealId, DealCommandRequest request,
        string correlationId, CancellationToken cancellationToken) => ChangeAsync(actor, dealId,
        Permissions.DealsEdit, "deal.awaiting_payment", correlationId, (deal, now) => deal.BeginPayment(
            request.CommandId, request.ExpectedVersion, actor.UserId, now), cancellationToken);

    public async Task<DealResponse> RegisterPaymentAsync(ActorContext actor, Guid dealId,
        RegisterDealPaymentRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var deal = await FindAsync(actor, dealId, Permissions.DealsPayments, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!deal.RegisterPayment(request.PaymentId, request.CommandId, request.Kind, request.Status,
                request.Amount, request.Currency, request.ManualReference, request.Reason, request.OccurredAt,
                request.ExpectedVersion, actor.UserId, now)) return Map(deal);
        if (deal.Status == DealStatus.Refunded)
            await ReleaseVehicleAsync(deal, actor.UserId, now, cancellationToken);
        WriteAudit(actor, request.Kind == PaymentKind.Refund ? "deal.refund_registered" : "deal.payment_registered",
            deal, correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return Map(deal);
    }

    public async Task<DealResponse> GenerateDocumentAsync(ActorContext actor, Guid dealId,
        GenerateDealDocumentRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var deal = await FindAsync(actor, dealId, Permissions.DealsDocuments, cancellationToken);
        var existing = deal.Documents.SingleOrDefault(x => x.Id == request.DocumentId || x.CommandId == request.CommandId);
        if (existing is not null)
        {
            if (existing.Type != request.Type || existing.GeneratedByUserId != actor.UserId)
                throw new ConflictException("deal.document_idempotency_conflict", "Document ID относится к другому payload.");
            return Map(deal);
        }
        var now = timeProvider.GetUtcNow();
        var revision = deal.Documents.Count(x => x.Type == request.Type) + 1;
        var number = $"DOS-{now:yyyyMMdd}-{deal.Id.ToString("N")[..8].ToUpperInvariant()}-{(request.Type == DealDocumentType.SaleContract ? "SC" : "HA")}-R{revision}";
        var bytes = pdfGenerator.Generate(new DealPdfModel(request.Type, number, 1, deal.Id,
            deal.CustomerNameSnapshot, deal.VehicleSnapshotJson, deal.LineItemsJson, deal.FinalTotalAmount,
            deal.Currency, now));
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var objectKey = $"{actor.OrganizationId:N}/deals/{deal.Id:N}/documents/{request.DocumentId:N}/{Guid.NewGuid():N}.pdf";
        await using var stream = new MemoryStream(bytes, writable: false);
        await documentStorage.PutAsync(objectKey, stream, bytes.Length, cancellationToken);
        try
        {
            if (!deal.AddDocument(request.DocumentId, request.CommandId, request.Type, number,
                    "DealerOS Demo Sale Documents", 1, sha256, objectKey, bytes.Length, request.Reason,
                    request.ExpectedVersion, actor.UserId, now)) return Map(deal);
            WriteAudit(actor, "deal.document_generated", deal, correlationId, now);
            await store.SaveChangesAsync(cancellationToken);
            return Map(deal);
        }
        catch
        {
            await SafeDeleteAsync(objectKey);
            store.ResetTracking();
            var current = await store.FindAsync(actor.OrganizationId, dealId, cancellationToken);
            if (current?.Documents.Any(x => x.Id == request.DocumentId && x.Type == request.Type
                    && x.GeneratedByUserId == actor.UserId) == true) return Map(current);
            throw;
        }
    }

    public async Task<DealDocumentDownload> DownloadDocumentAsync(ActorContext actor, Guid dealId,
        Guid documentId, string correlationId, CancellationToken cancellationToken)
    {
        var deal = await FindAsync(actor, dealId, Permissions.DealsView, cancellationToken);
        var document = deal.Documents.SingleOrDefault(x => x.Id == documentId)
            ?? throw new NotFoundException("Документ сделки не найден.");
        var stream = await documentStorage.GetAsync(document.ObjectKey, cancellationToken);
        audit.Write(actor.OrganizationId, actor.UserId, "deal.document_downloaded", "DealDocument", document.Id,
            null, JsonSerializer.Serialize(new { document.Type, document.Revision, document.Sha256 }),
            correlationId, timeProvider.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);
        return new DealDocumentDownload(stream, $"{document.Number}.pdf", "application/pdf");
    }

    public Task<DealResponse> MarkReadyForHandoverAsync(ActorContext actor, Guid dealId,
        DealCommandRequest request, string correlationId, CancellationToken cancellationToken) => ChangeAsync(actor,
        dealId, Permissions.DealsHandover, "deal.ready_for_handover", correlationId, (deal, now) =>
            deal.MarkReadyForHandover(request.CommandId, request.ExpectedVersion, actor.UserId, now),
        cancellationToken);

    public Task<DealResponse> CompleteHandoverAsync(ActorContext actor, Guid dealId,
        CompleteHandoverRequest request, string correlationId, CancellationToken cancellationToken) => ChangeAsync(
        actor, dealId, Permissions.DealsHandover, "deal.handover_completed", correlationId, (deal, now) =>
            deal.CompleteHandover(request.CommandId, request.ActualMileageKm, request.KeysTransferred,
                request.DocumentsTransferred, request.EquipmentTransferred, request.ConditionConfirmed,
                request.IssuerConfirmed, request.ResponsibleConfirmed, request.ConditionNotes, request.Comments,
                request.ExpectedVersion, actor.UserId, now), cancellationToken);

    public async Task<DealResponse> CompleteAsync(ActorContext actor, Guid dealId, DealCommandRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        var deal = await FindAsync(actor, dealId, Permissions.DealsHandover, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!deal.Complete(request.CommandId, request.ExpectedVersion, actor.UserId, now)) return Map(deal);
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, deal.VehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль сделки не найден.");
        vehicle.CompleteSale(now, actor.UserId);
        var listing = await store.FindListingAsync(actor.OrganizationId, deal.VehicleId, cancellationToken);
        if (listing is not null)
            foreach (var publication in listing.Publications.Where(x => x.Status == ChannelPublicationStatus.Published).ToArray())
                listing.Unpublish(Guid.NewGuid(), publication.Channel, actor.UserId, now);
        WriteAudit(actor, "deal.completed", deal, correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return Map(deal);
    }

    public async Task<DealResponse> CancelAsync(ActorContext actor, Guid dealId,
        DealReasonCommandRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var deal = await FindAsync(actor, dealId, Permissions.DealsCancel, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!deal.Cancel(request.CommandId, request.Reason, request.ExpectedVersion, actor.UserId, now)) return Map(deal);
        if (deal.Status == DealStatus.Cancelled)
            await ReleaseVehicleAsync(deal, actor.UserId, now, cancellationToken);
        WriteAudit(actor, "deal.cancelled", deal, correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return Map(deal);
    }

    private async Task<DealResponse> ChangeAsync(ActorContext actor, Guid dealId, string permission,
        string operation, string correlationId, Func<Deal, DateTimeOffset, bool> change,
        CancellationToken cancellationToken)
    {
        var deal = await FindAsync(actor, dealId, permission, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!change(deal, now)) return Map(deal);
        WriteAudit(actor, operation, deal, correlationId, now);
        await store.SaveChangesAsync(cancellationToken);
        return Map(deal);
    }

    private async Task<Deal?> FindExistingCreateAsync(ActorContext actor, CreateDealRequest request,
        CancellationToken cancellationToken)
    {
        var byId = await store.FindAsync(actor.OrganizationId, request.DealId, cancellationToken);
        var byCommand = await store.FindByCreateCommandAsync(actor.OrganizationId, request.CommandId,
            cancellationToken);
        if (byId is not null && byCommand is not null && byId.Id != byCommand.Id)
            throw new ConflictException("deal.command_conflict", "Command ID относится к другой сделке.");
        var existing = byId ?? byCommand;
        if (existing is null) return null;
        DemandBranch(actor, existing.BranchId);
        if (existing.CreatedByUserId != actor.UserId || existing.ReservationId != request.ReservationId)
            throw new ConflictException("deal.id_conflict", "Deal ID или command ID использован с другим payload.");
        return existing;
    }

    private static void EnsureSource(DealSource source, DateTimeOffset now)
    {
        if (source.Reservation.Status != ReservationStatus.Active || source.Reservation.ExpiresAt <= now)
            throw new ConflictException("deal.active_reservation_required", "Сделка требует действующую Active бронь.");
        if (source.Reservation.DepositStatus is not (ReservationDepositStatus.NotRequired
                or ReservationDepositStatus.Received))
            throw new ConflictException("deal.deposit_unresolved", "Статус предоплаты брони не завершён.");
        if (source.Offer.Status != SalesOfferStatus.Approved || source.Snapshot.Id != source.Reservation.ApprovedOfferSnapshotId
            || source.Offer.ApprovedSnapshot?.Id != source.Snapshot.Id)
            throw new ConflictException("deal.offer_snapshot_mismatch", "Бронь не соответствует Approved Offer snapshot.");
        if (source.Vehicle.Status != VehicleStatus.Reserved || source.Vehicle.Id != source.Reservation.VehicleId
            || source.Vehicle.BranchId != source.Reservation.BranchId)
            throw new ConflictException("deal.vehicle_not_reserved", "Автомобиль не зарезервирован для этой сделки.");
    }

    private async Task<Deal> FindAsync(ActorContext actor, Guid dealId, string permission,
        CancellationToken cancellationToken)
    {
        Demand(actor, permission);
        var deal = await store.FindAsync(actor.OrganizationId, dealId, cancellationToken)
            ?? throw new NotFoundException("Сделка не найдена.");
        DemandBranch(actor, deal.BranchId); return deal;
    }

    private async Task ReleaseVehicleAsync(Deal deal, Guid actorUserId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var vehicle = await store.FindVehicleAsync(deal.OrganizationId, deal.VehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль сделки не найден.");
        vehicle.CancelSale(now, actorUserId);
    }

    private static DealResponse Map(Deal deal) => new(deal.Id, deal.BranchId, deal.ReservationId,
        deal.ApprovedOfferSnapshotId, deal.CustomerId, deal.LeadId, deal.VehicleId, deal.CustomerNameSnapshot,
        deal.VehicleSnapshotJson, deal.LineItemsJson, deal.Status.ToString(), deal.BasePriceAmount,
        deal.LineItemsAmount, deal.DiscountAmount, deal.FinalTotalAmount, deal.CostSnapshotAmount,
        deal.ExpectedMarginAmount, deal.Currency, deal.ReceivedTotal, deal.RefundedTotal, deal.NetPaid, deal.Balance,
        deal.CreatedAt, deal.CompletedAt, deal.ClosedAt, deal.ClosureReason, deal.Version,
        deal.Payments.OrderBy(x => x.RecordedAt).Select(x => new DealPaymentResponse(x.Id, x.CommandId,
            x.Kind.ToString(), x.Status.ToString(), x.Amount, x.Currency, x.ManualReference, x.Reason,
            x.OccurredAt, x.RecordedAt)).ToArray(),
        deal.Documents.OrderBy(x => x.Type).ThenBy(x => x.Revision).Select(x => new DealDocumentResponse(x.Id,
            x.CommandId, x.Type.ToString(), x.Number, x.TemplateName, x.TemplateVersion, x.Revision,
            x.SourceDocumentId, x.Sha256, x.SizeBytes, x.Reason, x.GeneratedAt)).ToArray(),
        deal.Handover is { } h ? new DealHandoverResponse(h.ActualMileageKm, h.KeysTransferred,
            h.DocumentsTransferred, h.EquipmentTransferred, h.ConditionConfirmed, h.IssuerConfirmed,
            h.ResponsibleConfirmed, h.ConditionNotes, h.Comments, h.CompletedAt) : null,
        deal.History.OrderBy(x => x.OccurredAt).Select(x => new DealHistoryResponse(x.CommandId, x.Operation,
            x.OccurredAt)).ToArray());

    private void WriteAudit(ActorContext actor, string operation, Deal deal, string correlationId,
        DateTimeOffset now) => audit.Write(actor.OrganizationId, actor.UserId, operation, "Deal", deal.Id, null,
        JsonSerializer.Serialize(new
        {
            deal.BranchId,
            deal.ReservationId,
            deal.VehicleId,
            deal.ApprovedOfferSnapshotId,
            deal.Status,
            deal.FinalTotalAmount,
            deal.Currency,
            deal.ReceivedTotal,
            deal.RefundedTotal,
            deal.NetPaid,
            deal.Version
        }), correlationId, now);

    private async Task SafeDeleteAsync(string objectKey)
    { try { await documentStorage.DeleteAsync(objectKey, CancellationToken.None); } catch { /* retry-safe orphan is logged by adapter */ } }
    private static void Demand(ActorContext actor, string permission)
    { if (!actor.Permissions.Contains(permission)) throw new ForbiddenException("Недостаточно прав для сделки."); }
    private static void DemandBranch(ActorContext actor, Guid branchId)
    { if (!actor.BranchIds.Contains(branchId)) throw new ForbiddenException("Филиал недоступен пользователю."); }
}
