using System.Text.Json;
using DealerOS.Modules.Crm.Domain;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Sales.Domain;
using DealerOS.Modules.Vehicles.Application;
using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Sales.Application;

public sealed class SalesService(ISalesStore store, IAuditWriter audit, TimeProvider timeProvider)
{
    public async Task<VisitResponse> CreateVisitAsync(ActorContext actor, CreateVisitRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.SalesVisitsCreate);
        var existing = await store.FindVisitAsync(actor.OrganizationId, request.VisitId, cancellationToken);
        if (existing is not null)
        {
            DemandBranch(actor, existing.BranchId);
            if (!existing.MatchesCreate(existing.BranchId, existing.CustomerId, request.LeadId, existing.VehicleId,
                    existing.ResponsibleUserId, request.StartsAt, request.EndsAt, request.IncludesTestDrive))
                throw new ConflictException("sales.visit_id_conflict", "Visit ID использован с другим payload.");
            return await MapVisitAsync(actor, existing, cancellationToken);
        }
        var lead = await FindQualifiedLeadAsync(actor, request.LeadId, cancellationToken);
        if (lead.VehicleId is not { } vehicleId || lead.AssignedManagerUserId is not { } responsibleUserId)
            throw new DomainException("sales.visit_context_required", "Для визита lead должен иметь автомобиль и назначенного менеджера.");
        var vehicle = await FindAvailableVehicleAsync(actor, lead.BranchId, vehicleId, cancellationToken);
        if (!await store.IsActiveUserInBranchAsync(actor.OrganizationId, responsibleUserId, lead.BranchId,
                Permissions.SalesVisitsEdit, cancellationToken))
            throw new DomainException("sales.responsible_unavailable", "Ответственный менеджер недоступен в филиале.");
        await EnsureSlotAsync(actor.OrganizationId, vehicle.Id, responsibleUserId, request.StartsAt,
            request.EndsAt, null, request.IncludesTestDrive, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var visit = Visit.Create(request.VisitId, actor.OrganizationId, lead.BranchId, lead.CustomerId, lead.Id,
            vehicle.Id, responsibleUserId, request.StartsAt, request.EndsAt, request.IncludesTestDrive,
            actor.UserId, now);
        await store.AddVisitAsync(visit, cancellationToken);
        Audit(actor, "sales.visit_created", "Visit", visit.Id, new
        {
            visit.BranchId,
            visit.LeadId,
            visit.VehicleId,
            visit.ResponsibleUserId,
            visit.StartsAt,
            visit.EndsAt,
            visit.IncludesTestDrive
        }, correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapVisitAsync(actor, visit, cancellationToken);
    }

    public async Task<IReadOnlyList<VisitResponse>> ListVisitsAsync(ActorContext actor,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.SalesVisitsView); var result = new List<VisitResponse>();
        foreach (var visit in await store.ListVisitsAsync(actor.OrganizationId, actor.BranchIds, cancellationToken))
            result.Add(await MapVisitAsync(actor, visit, cancellationToken));
        return result;
    }

    public async Task<VisitResponse> GetVisitAsync(ActorContext actor, Guid visitId,
        CancellationToken cancellationToken) => await MapVisitAsync(actor,
        await FindVisitAsync(actor, visitId, Permissions.SalesVisitsView, cancellationToken), cancellationToken);

    public async Task<VisitResponse> RescheduleAsync(ActorContext actor, Guid visitId,
        RescheduleVisitRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var visit = await FindVisitAsync(actor, visitId, Permissions.SalesVisitsEdit, cancellationToken);
        await EnsureSlotAsync(actor.OrganizationId, visit.VehicleId, visit.ResponsibleUserId, request.StartsAt,
            request.EndsAt, visit.Id, visit.IncludesTestDrive, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!visit.Reschedule(request.CommandId, request.StartsAt, request.EndsAt, request.ExpectedVersion,
                actor.UserId, now)) return await MapVisitAsync(actor, visit, cancellationToken);
        AuditVisitCommand(actor, visit, "sales.visit_rescheduled", correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapVisitAsync(actor, visit, cancellationToken);
    }

    public Task<VisitResponse> ArriveAsync(ActorContext actor, Guid visitId, VisitCommandRequest request,
        string correlationId, CancellationToken cancellationToken) => ChangeVisitAsync(actor, visitId,
        Permissions.SalesVisitsEdit, request.ExpectedVersion, correlationId, "sales.visit_arrived", (visit, now) =>
            visit.Arrive(request.CommandId, request.ExpectedVersion, actor.UserId, now), cancellationToken);
    public Task<VisitResponse> CheckOutAsync(ActorContext actor, Guid visitId, TestDriveCheckOutRequest request,
        string correlationId, CancellationToken cancellationToken) => ChangeVisitAsync(actor, visitId,
        Permissions.SalesVisitsEdit, request.ExpectedVersion, correlationId, "sales.test_drive_checked_out",
        (visit, now) => visit.CheckOut(request.CommandId, request.DriverDocumentsChecked, request.IssueChecklist,
            request.OdometerOutKm, request.ConditionOut, request.ExpectedVersion, actor.UserId, now),
        cancellationToken);
    public Task<VisitResponse> CheckInAsync(ActorContext actor, Guid visitId, TestDriveCheckInRequest request,
        string correlationId, CancellationToken cancellationToken) => ChangeVisitAsync(actor, visitId,
        Permissions.SalesVisitsEdit, request.ExpectedVersion, correlationId, "sales.test_drive_checked_in",
        (visit, now) => visit.CheckIn(request.CommandId, request.ReturnChecklist, request.OdometerInKm,
            request.ConditionIn, request.IncidentOccurred, request.IncidentComment, request.ExpectedVersion,
            actor.UserId, now), cancellationToken);
    public Task<VisitResponse> CompleteVisitAsync(ActorContext actor, Guid visitId, CompleteVisitRequest request,
        string correlationId, CancellationToken cancellationToken) => ChangeVisitAsync(actor, visitId,
        Permissions.SalesVisitsComplete, request.ExpectedVersion, correlationId, "sales.visit_completed",
        (visit, now) => visit.Complete(request.CommandId, request.Result, request.NextAction,
            request.NextActionDueAt, request.ExpectedVersion, actor.UserId, now), cancellationToken);
    public Task<VisitResponse> NoShowAsync(ActorContext actor, Guid visitId, VisitReasonCommandRequest request,
        string correlationId, CancellationToken cancellationToken) => ChangeVisitAsync(actor, visitId,
        Permissions.SalesVisitsComplete, request.ExpectedVersion, correlationId, "sales.visit_no_show",
        (visit, now) => visit.NoShow(request.CommandId, request.Reason, request.ExpectedVersion, actor.UserId, now),
        cancellationToken);
    public Task<VisitResponse> CancelVisitAsync(ActorContext actor, Guid visitId, VisitReasonCommandRequest request,
        string correlationId, CancellationToken cancellationToken) => ChangeVisitAsync(actor, visitId,
        Permissions.SalesVisitsEdit, request.ExpectedVersion, correlationId, "sales.visit_cancelled",
        (visit, now) => visit.Cancel(request.CommandId, request.Reason, request.ExpectedVersion, actor.UserId, now),
        cancellationToken);

    public async Task<OfferPreviewResponse> PreviewOfferAsync(ActorContext actor, PreviewOfferRequest request,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.SalesOffersCreate);
        var lead = await FindQualifiedLeadAsync(actor, request.LeadId, cancellationToken);
        await EnsureCompletedVisitAsync(actor.OrganizationId, lead.Id, cancellationToken);
        var context = await GetPriceContextAsync(actor, lead, cancellationToken);
        return Preview(context, request.LineItems, request.DiscountAmount,
            await store.GetPolicyAsync(actor.OrganizationId, cancellationToken));
    }

    public async Task<SalesOfferResponse> CreateOfferAsync(ActorContext actor, CreateOfferRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.SalesOffersCreate);
        var existing = await store.FindOfferAsync(actor.OrganizationId, request.OfferId, cancellationToken);
        if (existing is not null)
        {
            DemandBranch(actor, existing.BranchId);
            if (!existing.MatchesCreate(existing.BranchId, existing.CustomerId, request.LeadId,
                    existing.VehicleId, actor.UserId, request.ValidUntil, MapLines(request.LineItems),
                    request.DiscountAmount))
                throw new ConflictException("sales.offer_id_conflict", "Offer ID использован с другим payload.");
            return await MapOfferAsync(actor, existing, cancellationToken);
        }
        var lead = await FindQualifiedLeadAsync(actor, request.LeadId, cancellationToken);
        await EnsureCompletedVisitAsync(actor.OrganizationId, lead.Id, cancellationToken);
        var context = await GetPriceContextAsync(actor, lead, cancellationToken);
        var policy = await store.GetPolicyAsync(actor.OrganizationId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var offer = SalesOffer.Create(request.OfferId, actor.OrganizationId, lead.BranchId, lead.CustomerId,
            lead.Id, context.Vehicle.Id, actor.UserId, context.BasePrice, context.CostSnapshot,
            policy.MinimumMarginAmount, context.Currency, request.ValidUntil, MapLines(request.LineItems),
            request.DiscountAmount, now);
        await store.AddOfferAsync(offer, cancellationToken);
        AuditOffer(actor, offer, "sales.offer_created", correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapOfferAsync(actor, offer, cancellationToken);
    }

    public async Task<SalesOfferResponse> UpdateOfferAsync(ActorContext actor, Guid offerId,
        UpdateOfferRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var offer = await FindOfferAsync(actor, offerId, Permissions.SalesOffersEdit, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!offer.Update(request.CommandId, MapLines(request.LineItems), request.DiscountAmount,
                request.ValidUntil, request.ExpectedVersion, actor.UserId, now))
            return await MapOfferAsync(actor, offer, cancellationToken);
        AuditOffer(actor, offer, "sales.offer_updated", correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapOfferAsync(actor, offer, cancellationToken);
    }

    public async Task<SalesOfferResponse> SubmitOfferAsync(ActorContext actor, Guid offerId,
        SubmitOfferRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var offer = await FindOfferAsync(actor, offerId, Permissions.SalesOffersSubmit, cancellationToken);
        var policy = await store.GetPolicyAsync(actor.OrganizationId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!offer.Submit(request.CommandId, actor.Permissions.Contains(Permissions.SalesOffersAutoApprove),
                policy.AutoApprovalDiscountLimit, request.ExpectedVersion, actor.UserId, now))
            return await MapOfferAsync(actor, offer, cancellationToken);
        AuditOffer(actor, offer, offer.Status == SalesOfferStatus.Approved ? "sales.offer_auto_approved"
            : "sales.offer_submitted", correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapOfferAsync(actor, offer, cancellationToken);
    }

    public async Task<SalesOfferResponse> DecideOfferAsync(ActorContext actor, Guid offerId,
        DecideOfferRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var offer = await FindOfferAsync(actor, offerId, Permissions.SalesOffersApprove, cancellationToken);
        if (offer.CreatedByUserId == actor.UserId)
            throw new ForbiddenException("Нельзя согласовать собственное предложение, требующее manager decision.");
        var now = timeProvider.GetUtcNow();
        if (!offer.Decide(request.DecisionId, request.Decision, request.Reason, request.ExpectedVersion,
                actor.UserId, now)) return await MapOfferAsync(actor, offer, cancellationToken);
        AuditOffer(actor, offer, $"sales.offer_{request.Decision.ToString().ToLowerInvariant()}", correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapOfferAsync(actor, offer, cancellationToken);
    }

    public async Task<SalesOfferResponse> CreateRevisionAsync(ActorContext actor, Guid offerId,
        CreateOfferRevisionRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var offer = await FindOfferAsync(actor, offerId, Permissions.SalesOffersEdit, cancellationToken);
        var existing = await store.FindOfferAsync(actor.OrganizationId, request.RevisionId, cancellationToken);
        if (existing is not null)
        {
            if (existing.RevisesOfferId != offer.Id || existing.ValidUntil != request.ValidUntil
                || existing.CreatedByUserId != actor.UserId)
                throw new ConflictException("sales.revision_id_conflict", "Revision ID относится к другому offer.");
            return await MapOfferAsync(actor, existing, cancellationToken);
        }
        var now = timeProvider.GetUtcNow();
        var revision = offer.CreateRevision(request.CommandId, request.RevisionId, request.ExpectedVersion,
            actor.UserId, request.ValidUntil, now);
        await store.AddOfferAsync(revision, cancellationToken);
        AuditOffer(actor, revision, "sales.offer_revision_created", correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapOfferAsync(actor, revision, cancellationToken);
    }

    public Task<SalesOfferResponse> CancelOfferAsync(ActorContext actor, Guid offerId,
        OfferReasonCommandRequest request, string correlationId, CancellationToken cancellationToken) =>
        ChangeOfferAsync(actor, offerId, Permissions.SalesOffersCancel, correlationId, "sales.offer_cancelled",
            (offer, now) => offer.Cancel(request.CommandId, request.Reason, request.ExpectedVersion, actor.UserId,
                now), cancellationToken);
    public Task<SalesOfferResponse> ExpireOfferAsync(ActorContext actor, Guid offerId, SubmitOfferRequest request,
        string correlationId, CancellationToken cancellationToken) => ChangeOfferAsync(actor, offerId,
        Permissions.SalesOffersEdit, correlationId, "sales.offer_expired", (offer, now) => offer.Expire(
            request.CommandId, request.ExpectedVersion, actor.UserId, now), cancellationToken);

    public async Task<IReadOnlyList<SalesOfferResponse>> ListOffersAsync(ActorContext actor,
        CancellationToken cancellationToken)
    {
        Demand(actor, Permissions.SalesOffersView); var result = new List<SalesOfferResponse>();
        foreach (var offer in await store.ListOffersAsync(actor.OrganizationId, actor.BranchIds, cancellationToken))
            result.Add(await MapOfferAsync(actor, offer, cancellationToken));
        return result;
    }
    public async Task<SalesOfferResponse> GetOfferAsync(ActorContext actor, Guid offerId,
        CancellationToken cancellationToken) => await MapOfferAsync(actor,
        await FindOfferAsync(actor, offerId, Permissions.SalesOffersView, cancellationToken), cancellationToken);

    private async Task<VisitResponse> ChangeVisitAsync(ActorContext actor, Guid visitId, string permission,
        long expectedVersion, string correlationId, string operation, Func<Visit, DateTimeOffset, bool> change,
        CancellationToken cancellationToken)
    {
        var visit = await FindVisitAsync(actor, visitId, permission, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!change(visit, now)) return await MapVisitAsync(actor, visit, cancellationToken);
        AuditVisitCommand(actor, visit, operation, correlationId, now);
        await store.SaveChangesAsync(cancellationToken); return await MapVisitAsync(actor, visit, cancellationToken);
    }
    private async Task<SalesOfferResponse> ChangeOfferAsync(ActorContext actor, Guid offerId, string permission,
        string correlationId, string operation, Func<SalesOffer, DateTimeOffset, bool> change,
        CancellationToken cancellationToken)
    {
        var offer = await FindOfferAsync(actor, offerId, permission, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!change(offer, now)) return await MapOfferAsync(actor, offer, cancellationToken);
        AuditOffer(actor, offer, operation, correlationId, now); await store.SaveChangesAsync(cancellationToken);
        return await MapOfferAsync(actor, offer, cancellationToken);
    }
    private async Task<Lead> FindQualifiedLeadAsync(ActorContext actor, Guid leadId,
        CancellationToken cancellationToken)
    {
        var lead = await store.FindLeadAsync(actor.OrganizationId, leadId, cancellationToken)
            ?? throw new NotFoundException("Lead не найден.");
        DemandBranch(actor, lead.BranchId);
        if (lead.Status != LeadStatus.Qualified)
            throw new DomainException("sales.qualified_lead_required", "Визит и offer доступны для Qualified lead.");
        return lead;
    }
    private async Task<Vehicle> FindAvailableVehicleAsync(ActorContext actor, Guid branchId, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        DemandBranch(actor, branchId);
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        if (vehicle.BranchId != branchId || vehicle.Status != VehicleStatus.ReadyForSale)
            throw new DomainException("sales.vehicle_not_available", "Автомобиль должен быть ReadyForSale в том же филиале.");
        return vehicle;
    }
    private async Task EnsureSlotAsync(Guid organizationId, Guid vehicleId, Guid responsibleUserId,
        DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? excludeVisitId, bool includesTestDrive,
        CancellationToken cancellationToken)
    {
        if (await store.HasVisitOverlapAsync(organizationId, vehicleId, responsibleUserId, startsAt, endsAt,
                excludeVisitId, includesTestDrive, cancellationToken))
            throw new ConflictException("sales.visit_slot_conflict", "Автомобиль или менеджер занят в выбранном слоте.");
    }
    private async Task<(Vehicle Vehicle, decimal BasePrice, decimal CostSnapshot, string Currency)> GetPriceContextAsync(
        ActorContext actor, Lead lead, CancellationToken cancellationToken)
    {
        if (lead.VehicleId is not { } vehicleId)
            throw new DomainException("sales.offer_vehicle_required", "Offer требует конкретный автомобиль.");
        var vehicle = await FindAvailableVehicleAsync(actor, lead.BranchId, vehicleId, cancellationToken);
        var listing = await store.FindReadyListingAsync(actor.OrganizationId, vehicle.Id, cancellationToken)
            ?? throw new DomainException("sales.listing_ready_required", "Offer требует Listing Ready.");
        var execution = await store.FindCompletedExecutionAsync(actor.OrganizationId, vehicle.Id, cancellationToken)
            ?? throw new DomainException("sales.confirmed_cost_required", "Нет завершённой подтверждённой подготовки.");
        if (listing.Currency != vehicle.Currency || listing.Currency != execution.Currency)
            throw new DomainException("sales.cost_currency_mismatch", "Цена и подтверждённая себестоимость имеют разные валюты.");
        return (vehicle, listing.PublicPriceAmount,
            vehicle.PlannedPurchaseAmount + execution.ActualTotalAmount, listing.Currency);
    }
    private static OfferPreviewResponse Preview((Vehicle Vehicle, decimal BasePrice, decimal CostSnapshot,
        string Currency) context, IReadOnlyList<OfferLineRequest> lines, decimal discount, SalesPolicy policy)
    {
        var normalized = SalesOffer.ValidateLines(MapLines(lines), context.Currency);
        var lineTotal = normalized.Sum(x => x.Amount);
        var safeDiscount = new Money(discount, context.Currency).Amount;
        var final = context.BasePrice + lineTotal - safeDiscount;
        if (final <= 0 || safeDiscount > context.BasePrice + lineTotal)
            throw new DomainException("sales.offer_total_not_positive", "Итоговая цена должна быть положительной.");
        var margin = final - context.CostSnapshot; var below = margin < policy.MinimumMarginAmount;
        return new OfferPreviewResponse(context.BasePrice, lineTotal, safeDiscount, final, context.CostSnapshot,
            margin, policy.MinimumMarginAmount, context.Currency, below,
            safeDiscount > policy.AutoApprovalDiscountLimit || below, policy.AutoApprovalDiscountLimit);
    }
    private static IEnumerable<OfferLineDraft> MapLines(IEnumerable<OfferLineRequest> lines) => lines.Select(x =>
        new OfferLineDraft(x.Id, x.Category ?? string.Empty, x.Name ?? string.Empty, x.Amount,
            x.Currency ?? string.Empty));
    private async Task<Visit> FindVisitAsync(ActorContext actor, Guid visitId, string permission,
        CancellationToken cancellationToken)
    {
        Demand(actor, permission); var visit = await store.FindVisitAsync(actor.OrganizationId, visitId,
            cancellationToken) ?? throw new NotFoundException("Визит не найден."); DemandBranch(actor, visit.BranchId);
        return visit;
    }
    private async Task EnsureCompletedVisitAsync(Guid organizationId, Guid leadId,
        CancellationToken cancellationToken)
    {
        if (!await store.HasCompletedVisitAsync(organizationId, leadId, cancellationToken))
            throw new DomainException("sales.completed_visit_required", "Offer доступен после завершённого визита.");
    }
    private async Task<SalesOffer> FindOfferAsync(ActorContext actor, Guid offerId, string permission,
        CancellationToken cancellationToken)
    {
        Demand(actor, permission); var offer = await store.FindOfferAsync(actor.OrganizationId, offerId,
            cancellationToken) ?? throw new NotFoundException("Offer не найден."); DemandBranch(actor, offer.BranchId);
        return offer;
    }
    private async Task<VisitResponse> MapVisitAsync(ActorContext actor, Visit visit,
        CancellationToken cancellationToken)
    {
        var customer = await store.FindCustomerAsync(actor.OrganizationId, visit.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer не найден.");
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, visit.VehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        var responsible = await store.FindUserNameAsync(actor.OrganizationId, visit.ResponsibleUserId,
            cancellationToken) ?? "Неизвестный пользователь";
        return new VisitResponse(visit.Id, visit.BranchId, visit.CustomerId, customer.Name, visit.LeadId,
            visit.VehicleId, $"{vehicle.Make} {vehicle.Model}", visit.ResponsibleUserId, responsible,
            visit.Status.ToString(), visit.StartsAt, visit.EndsAt, visit.IncludesTestDrive,
            visit.DriverDocumentsChecked, visit.IssueChecklist, visit.ReturnChecklist, visit.OdometerOutKm,
            visit.OdometerInKm, visit.ConditionOut, visit.ConditionIn, visit.CheckedOutAt, visit.CheckedInAt,
            visit.IncidentOccurred, visit.IncidentComment, visit.Result, visit.NextAction, visit.NextActionDueAt,
            visit.ClosureReason, visit.Version, visit.History.OrderBy(x => x.OccurredAt).Select(x =>
                new VisitHistoryResponse(x.CommandId, x.Operation, x.OccurredAt)).ToArray());
    }
    private async Task<SalesOfferResponse> MapOfferAsync(ActorContext actor, SalesOffer offer,
        CancellationToken cancellationToken)
    {
        var customer = await store.FindCustomerAsync(actor.OrganizationId, offer.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer не найден.");
        var vehicle = await store.FindVehicleAsync(actor.OrganizationId, offer.VehicleId, cancellationToken)
            ?? throw new NotFoundException("Автомобиль не найден.");
        var snapshot = offer.ApprovedSnapshot is { } x ? new ApprovedOfferSnapshotResponse(x.Id, x.Revision,
            x.BasePriceAmount, x.LineItemsAmount, x.DiscountAmount, x.FinalPriceAmount, x.CostSnapshotAmount,
            x.ExpectedMarginAmount, x.MinimumMarginAmount, x.Currency, x.ValidUntil, x.LineItemsJson,
            x.ApprovedByUserId, x.ApprovedAt) : null;
        var policy = await store.GetPolicyAsync(actor.OrganizationId, cancellationToken);
        return new SalesOfferResponse(offer.Id, offer.BranchId, offer.CustomerId, customer.Name, offer.LeadId,
            offer.VehicleId, $"{vehicle.Make} {vehicle.Model}", offer.RevisesOfferId, offer.Revision,
            offer.Status.ToString(), offer.BasePriceAmount, offer.LineItemsAmount, offer.DiscountAmount,
            offer.FinalPriceAmount, offer.CostSnapshotAmount, offer.ExpectedMarginAmount,
            offer.MinimumMarginAmount, offer.Currency, offer.ValidUntil, offer.IsBelowMinimumMargin,
            offer.DiscountAmount > policy.AutoApprovalDiscountLimit || offer.IsBelowMinimumMargin,
            policy.AutoApprovalDiscountLimit, offer.Version,
            offer.LineItems.Select(line => new OfferLineResponse(line.Id, line.Category, line.Name, line.Amount,
                line.Currency)).ToArray(), offer.Decisions.OrderBy(x => x.OccurredAt).Select(d =>
                new OfferDecisionResponse(d.Id, d.Type.ToString(), d.Reason, d.ActorUserId, d.OccurredAt)).ToArray(),
            offer.History.OrderBy(x => x.OccurredAt).Select(h => new OfferHistoryResponse(h.CommandId, h.Operation,
                h.OccurredAt)).ToArray(), snapshot);
    }
    private void AuditVisitCommand(ActorContext actor, Visit visit, string operation, string correlationId,
        DateTimeOffset now) => Audit(actor, operation, "Visit", visit.Id, new { visit.Status, visit.Version },
        correlationId, now);
    private void AuditOffer(ActorContext actor, SalesOffer offer, string operation, string correlationId,
        DateTimeOffset now) => Audit(actor, operation, "SalesOffer", offer.Id, new
        {
            offer.Status,
            offer.Revision,
            offer.BasePriceAmount,
            offer.LineItemsAmount,
            offer.DiscountAmount,
            offer.FinalPriceAmount,
            offer.CostSnapshotAmount,
            offer.ExpectedMarginAmount,
            offer.Currency
        }, correlationId, now);
    private void Audit(ActorContext actor, string operation, string entityType, Guid entityId, object value,
        string correlationId, DateTimeOffset now) => audit.Write(actor.OrganizationId, actor.UserId, operation,
        entityType, entityId, null, JsonSerializer.Serialize(value), correlationId, now);
    private static void Demand(ActorContext actor, string permission)
    { if (!actor.Permissions.Contains(permission)) throw new ForbiddenException("Недостаточно прав для продаж."); }
    private static void DemandBranch(ActorContext actor, Guid branchId)
    { if (!actor.BranchIds.Contains(branchId)) throw new ForbiddenException("Филиал недоступен пользователю."); }
}
