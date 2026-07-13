using DealerOS.Modules.Sales.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.UnitTests;

public sealed class SalesDomainTests
{
    [Fact]
    public void TestDriveRequiresDocumentCheckAndCompleteReturnEvidence()
    {
        var now = DateTimeOffset.UtcNow;
        var actor = Guid.NewGuid();
        var visit = Visit.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), now.AddHours(1), now.AddHours(2), true, actor, now);
        visit.Arrive(Guid.NewGuid(), visit.Version, actor, now.AddHours(1));
        Assert.Throws<DomainException>(() => visit.CheckOut(Guid.NewGuid(), false, "Кузов без повреждений", 42_000,
            "Без замечаний", visit.Version, actor, now.AddHours(1)));
        var checkout = Guid.NewGuid();
        Assert.True(visit.CheckOut(checkout, true, "Ключ и документы выданы", 42_000, "Без замечаний",
            visit.Version, actor, now.AddHours(1)));
        Assert.False(visit.CheckOut(checkout, true, "Ключ и документы выданы", 42_000, "Без замечаний", 1,
            actor, now.AddHours(1)));
        Assert.Throws<DomainException>(() => visit.Complete(Guid.NewGuid(), "Интерес подтверждён", "Offer",
            now.AddDays(1), visit.Version, actor, now.AddHours(2)));
        visit.CheckIn(Guid.NewGuid(), "Ключ и автомобиль возвращены", 42_012, "Без новых повреждений", false,
            null, visit.Version, actor, now.AddHours(2));
        visit.Complete(Guid.NewGuid(), "Интерес подтверждён", "Подготовить offer", now.AddDays(1), visit.Version,
            actor, now.AddHours(2));
        Assert.Equal(VisitStatus.Completed, visit.Status);
        Assert.Equal(12, visit.OdometerInKm - visit.OdometerOutKm);
    }

    [Fact]
    public void OfferRecalculatesTotalsAndAutoApprovesOnlyInsidePolicy()
    {
        var now = DateTimeOffset.UtcNow; var actor = Guid.NewGuid();
        var offer = CreateOffer(now, actor, 1_500_000m, 1_200_000m, 100_000m, 40_000m,
            [new OfferLineDraft(Guid.NewGuid(), "Equipment", "Зимние шины", 50_000m, "RUB")]);
        Assert.Equal(1_510_000m, offer.FinalPriceAmount);
        Assert.Equal(310_000m, offer.ExpectedMarginAmount);
        var submit = Guid.NewGuid();
        Assert.True(offer.Submit(submit, true, 50_000m, offer.Version, actor, now.AddMinutes(1)));
        Assert.Equal(SalesOfferStatus.Approved, offer.Status);
        Assert.NotNull(offer.ApprovedSnapshot);
        Assert.Equal(1_510_000m, offer.ApprovedSnapshot!.FinalPriceAmount);
        Assert.False(offer.Submit(submit, true, 50_000m, 1, actor, now.AddMinutes(2)));
        Assert.Throws<DomainException>(() => offer.Update(Guid.NewGuid(), [], 0, now.AddDays(2), offer.Version,
            actor, now.AddMinutes(2)));
    }

    [Fact]
    public void BelowMarginOfferNeedsExplicitCompatibleDecision()
    {
        var now = DateTimeOffset.UtcNow; var author = Guid.NewGuid(); var manager = Guid.NewGuid();
        var offer = CreateOffer(now, author, 1_300_000m, 1_250_000m, 100_000m, 100_000m, []);
        offer.Submit(Guid.NewGuid(), true, 50_000m, offer.Version, author, now.AddMinutes(1));
        Assert.Equal(SalesOfferStatus.Submitted, offer.Status);
        Assert.True(offer.IsBelowMinimumMargin);
        var decision = Guid.NewGuid();
        Assert.True(offer.Decide(decision, OfferDecisionType.Approved, "Осознанное решение руководителя",
            offer.Version, manager, now.AddMinutes(2)));
        Assert.False(offer.Decide(decision, OfferDecisionType.Approved, "Осознанное решение руководителя", 1,
            manager, now.AddMinutes(3)));
        Assert.Throws<ConflictException>(() => offer.Decide(decision, OfferDecisionType.Approved,
            "Другая причина", 1, manager, now.AddMinutes(3)));
        Assert.Equal(SalesOfferStatus.Approved, offer.Status);
    }

    [Fact]
    public void ApprovedOfferCreatesIndependentDraftRevision()
    {
        var now = DateTimeOffset.UtcNow; var author = Guid.NewGuid();
        var offer = CreateOffer(now, author, 1_500_000m, 1_200_000m, 100_000m, 0,
            [new OfferLineDraft(Guid.NewGuid(), "Service", "Гарантия", 30_000m, "RUB")]);
        offer.Submit(Guid.NewGuid(), true, 50_000m, offer.Version, author, now.AddMinutes(1));
        var snapshotJson = offer.ApprovedSnapshot!.LineItemsJson;
        var revision = offer.CreateRevision(Guid.NewGuid(), Guid.NewGuid(), offer.Version, author,
            now.AddDays(14), now.AddMinutes(2));
        Assert.Equal(SalesOfferStatus.Draft, revision.Status);
        Assert.Equal(2, revision.Revision);
        Assert.Equal(offer.Id, revision.RevisesOfferId);
        revision.Update(Guid.NewGuid(), [], 10_000m, now.AddDays(7), revision.Version, author, now.AddMinutes(3));
        Assert.Equal(snapshotJson, offer.ApprovedSnapshot.LineItemsJson);
        Assert.Equal(SalesOfferStatus.Approved, offer.Status);
    }

    [Fact]
    public void CreateIdempotencyKeepsTheOriginalPayloadAfterLaterEdits()
    {
        var now = DateTimeOffset.UtcNow; var actor = Guid.NewGuid();
        var branch = Guid.NewGuid(); var customer = Guid.NewGuid(); var lead = Guid.NewGuid();
        var vehicle = Guid.NewGuid(); var responsible = Guid.NewGuid();
        var starts = now.AddHours(1); var ends = starts.AddHours(1);
        var visit = Visit.Create(Guid.NewGuid(), Guid.NewGuid(), branch, customer, lead, vehicle, responsible,
            starts, ends, true, actor, now);
        visit.Reschedule(Guid.NewGuid(), starts.AddDays(1), ends.AddDays(1), visit.Version, actor, now.AddMinutes(1));
        Assert.True(visit.MatchesCreate(branch, customer, lead, vehicle, responsible, starts, ends, true));
        Assert.False(visit.MatchesCreate(branch, customer, lead, vehicle, responsible, starts.AddDays(1),
            ends.AddDays(1), true));

        var validUntil = now.AddDays(10);
        var originalLine = new OfferLineDraft(Guid.NewGuid(), "Equipment", "Комплект", 20_000m, "RUB");
        var offer = SalesOffer.Create(Guid.NewGuid(), Guid.NewGuid(), branch, customer, lead, vehicle, actor,
            1_500_000m, 1_100_000m, 100_000m, "RUB", validUntil, [originalLine], 10_000m, now);
        offer.Update(Guid.NewGuid(), [], 0, now.AddDays(11), offer.Version, actor, now.AddMinutes(1));
        Assert.True(offer.MatchesCreate(branch, customer, lead, vehicle, actor, validUntil, [originalLine],
            10_000m));
        Assert.False(offer.MatchesCreate(branch, customer, lead, vehicle, actor, now.AddDays(11), [], 0));
    }

    [Fact]
    public void OfferRejectsNegativeOrMixedCurrencyLines()
    {
        var now = DateTimeOffset.UtcNow; var actor = Guid.NewGuid();
        Assert.Throws<DomainException>(() => CreateOffer(now, actor, 1_500_000m, 1_100_000m, 100_000m, 0,
            [new OfferLineDraft(Guid.NewGuid(), "Equipment", "Недопустимая строка", -1m, "RUB")]));
        Assert.Throws<DomainException>(() => CreateOffer(now, actor, 1_500_000m, 1_100_000m, 100_000m, 0,
            [new OfferLineDraft(Guid.NewGuid(), "Equipment", "Другая валюта", 100m, "USD")]));
    }

    private static SalesOffer CreateOffer(DateTimeOffset now, Guid actor, decimal price, decimal cost,
        decimal minimumMargin, decimal discount, IReadOnlyList<OfferLineDraft> lines) => SalesOffer.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), actor,
        price, cost, minimumMargin, "RUB", now.AddDays(10), lines, discount, now);
}
