using DealerOS.Modules.Deals.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.UnitTests;

public sealed class DealDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DepositTransferAndPaymentLedgerReconcileWithoutDoubleCounting()
    {
        var actor = Guid.NewGuid();
        var deal = Create(actor, true, 100_000m);
        Assert.Equal(100_000m, deal.ReceivedTotal);
        Assert.Equal(1_400_000m, deal.Balance);
        deal.BeginPayment(Guid.NewGuid(), deal.Version, actor, Now.AddMinutes(1));
        var paymentId = Guid.NewGuid(); var commandId = Guid.NewGuid();
        Assert.True(deal.RegisterPayment(paymentId, commandId, PaymentKind.Payment, PaymentStatus.Received,
            1_400_000m, "RUB", "DEMO-FINAL-001", "Полная оплата", Now.AddMinutes(2), deal.Version,
            actor, Now.AddMinutes(2)));
        Assert.Equal(1_500_000m, deal.NetPaid);
        Assert.Equal(0m, deal.Balance);
        Assert.False(deal.RegisterPayment(paymentId, commandId, PaymentKind.Payment, PaymentStatus.Received,
            1_400_000m, "RUB", "DEMO-FINAL-001", "Полная оплата", Now.AddMinutes(2), 1,
            actor, Now.AddMinutes(3)));
        Assert.Throws<ConflictException>(() => deal.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(),
            PaymentKind.Payment, PaymentStatus.Received, 1m, "RUB", "DEMO-FINAL-001", "Другой payload",
            Now.AddMinutes(3), deal.Version, actor, Now.AddMinutes(3)));
    }

    [Fact]
    public void ReadyForHandoverRequiresPaymentAndBothDocumentTypes()
    {
        var actor = Guid.NewGuid(); var deal = Create(actor);
        deal.BeginPayment(Guid.NewGuid(), deal.Version, actor, Now.AddMinutes(1));
        Assert.Throws<DomainException>(() => deal.MarkReadyForHandover(Guid.NewGuid(), deal.Version, actor,
            Now.AddMinutes(2)));
        deal.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), PaymentKind.Payment, PaymentStatus.Received,
            1_500_000m, "RUB", "DEMO-PAID", "Оплата", Now.AddMinutes(2), deal.Version, actor,
            Now.AddMinutes(2));
        AddDocument(deal, DealDocumentType.SaleContract, actor);
        Assert.Throws<DomainException>(() => deal.MarkReadyForHandover(Guid.NewGuid(), deal.Version, actor,
            Now.AddMinutes(3)));
        AddDocument(deal, DealDocumentType.HandoverAct, actor);
        deal.MarkReadyForHandover(Guid.NewGuid(), deal.Version, actor, Now.AddMinutes(4));
        Assert.Equal(DealStatus.ReadyForHandover, deal.Status);
    }

    [Fact]
    public void HandoverChecklistAndCompletionAreImmutableGates()
    {
        var actor = Guid.NewGuid(); var deal = Ready(actor);
        Assert.Throws<DomainException>(() => deal.CompleteHandover(Guid.NewGuid(), 42_100, true, true, false,
            true, true, true, "Без замечаний", null, deal.Version, actor, Now.AddMinutes(5)));
        deal.CompleteHandover(Guid.NewGuid(), 42_100, true, true, true, true, true, true,
            "Состояние соответствует snapshot", "Выдано в demo", deal.Version, actor, Now.AddMinutes(5));
        deal.Complete(Guid.NewGuid(), deal.Version, actor, Now.AddMinutes(6));
        Assert.Equal(DealStatus.Completed, deal.Status);
        Assert.Throws<DomainException>(() => deal.Cancel(Guid.NewGuid(), "Поздняя отмена", deal.Version, actor,
            Now.AddMinutes(7)));
    }

    [Fact]
    public void CancellationWithMoneyRequiresExactRefundAndPreservesLedger()
    {
        var actor = Guid.NewGuid(); var deal = Create(actor, true, 100_000m);
        deal.BeginPayment(Guid.NewGuid(), deal.Version, actor, Now.AddMinutes(1));
        deal.Cancel(Guid.NewGuid(), "Клиент отказался", deal.Version, actor, Now.AddMinutes(2));
        Assert.Equal(DealStatus.RefundPending, deal.Status);
        Assert.Throws<DomainException>(() => deal.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), PaymentKind.Refund,
            PaymentStatus.Refunded, 100_001m, "RUB", "DEMO-REFUND-BAD", "Возврат", Now.AddMinutes(3),
            deal.Version, actor, Now.AddMinutes(3)));
        deal.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), PaymentKind.Refund, PaymentStatus.Refunded,
            100_000m, "RUB", "DEMO-REFUND-001", "Возврат предоплаты", Now.AddMinutes(3), deal.Version,
            actor, Now.AddMinutes(3));
        Assert.Equal(DealStatus.Refunded, deal.Status);
        Assert.Equal(0m, deal.NetPaid);
        Assert.Equal(2, deal.Payments.Count);
    }

    [Fact]
    public void DocumentRegenerationCreatesLinkedRevisionAndRequiresReason()
    {
        var actor = Guid.NewGuid(); var deal = Create(actor);
        AddDocument(deal, DealDocumentType.SaleContract, actor);
        Assert.Throws<DomainException>(() => deal.AddDocument(Guid.NewGuid(), Guid.NewGuid(),
            DealDocumentType.SaleContract, "DOS-2", "Demo", 1, new string('b', 64), "key-2", 200,
            null, deal.Version, actor, Now.AddMinutes(2)));
        deal.AddDocument(Guid.NewGuid(), Guid.NewGuid(), DealDocumentType.SaleContract, "DOS-2", "Demo", 1,
            new string('b', 64), "key-2", 200, "Исправлен номер", deal.Version, actor, Now.AddMinutes(2));
        var latest = deal.Documents.OrderByDescending(x => x.Revision).First();
        Assert.Equal(2, latest.Revision);
        Assert.NotNull(latest.SourceDocumentId);
    }

    private static Deal Ready(Guid actor)
    {
        var deal = Create(actor); deal.BeginPayment(Guid.NewGuid(), deal.Version, actor, Now.AddMinutes(1));
        deal.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), PaymentKind.Payment, PaymentStatus.Received,
            1_500_000m, "RUB", "DEMO-READY", "Полная оплата", Now.AddMinutes(2), deal.Version, actor,
            Now.AddMinutes(2));
        AddDocument(deal, DealDocumentType.SaleContract, actor);
        AddDocument(deal, DealDocumentType.HandoverAct, actor);
        deal.MarkReadyForHandover(Guid.NewGuid(), deal.Version, actor, Now.AddMinutes(4)); return deal;
    }

    private static void AddDocument(Deal deal, DealDocumentType type, Guid actor) => deal.AddDocument(
        Guid.NewGuid(), Guid.NewGuid(), type, $"DOS-{type}", "Demo", 1, new string('a', 64),
        $"key-{type}", 100, null, deal.Version, actor, Now.AddMinutes(3));

    private static Deal Create(Guid actor, bool depositReceived = false, decimal depositAmount = 0) => Deal.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), actor, Guid.NewGuid(), "Иван Демо", "{\"vin\":\"WVWZZZ1JZXW123456\"}", "[]",
        1_500_000m, 0, 0, 1_500_000m, 1_100_000m, 400_000m, "RUB", depositReceived, depositAmount,
        depositReceived ? "DEMO-DEPOSIT" : null, Now);
}
