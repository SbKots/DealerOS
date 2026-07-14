using DealerOS.Modules.Reservations.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.UnitTests;

public sealed class ReservationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequiredDepositMovesReservationFromPendingToActiveIdempotently()
    {
        var actor = Guid.NewGuid();
        var reservation = Create(true, 100_000m, actor);
        Assert.Equal(ReservationStatus.PendingDeposit, reservation.Status);
        Assert.Equal(ReservationDepositStatus.Pending, reservation.DepositStatus);

        var commandId = Guid.NewGuid();
        Assert.True(reservation.RegisterDeposit(commandId, ReservationDepositStatus.Received, "DEMO-001", null,
            reservation.Version, actor, Now.AddMinutes(1)));
        Assert.Equal(ReservationStatus.Active, reservation.Status);
        Assert.Equal(ReservationDepositStatus.Received, reservation.DepositStatus);
        Assert.False(reservation.RegisterDeposit(commandId, ReservationDepositStatus.Received, "DEMO-001", null,
            1, actor, Now.AddMinutes(2)));
        Assert.Throws<ConflictException>(() => reservation.RegisterDeposit(commandId,
            ReservationDepositStatus.Failed, "DEMO-001", "Другая операция", 1, actor, Now.AddMinutes(2)));
    }

    [Fact]
    public void FailedDepositClosesReservationAndPreventsFurtherChanges()
    {
        var actor = Guid.NewGuid();
        var reservation = Create(true, 50_000m, actor);
        reservation.RegisterDeposit(Guid.NewGuid(), ReservationDepositStatus.Failed, null, "Платёж отклонён",
            reservation.Version, actor, Now.AddMinutes(1));
        Assert.Equal(ReservationStatus.DepositFailed, reservation.Status);
        Assert.NotNull(reservation.ClosedAt);
        Assert.Throws<DomainException>(() => reservation.Extend(Guid.NewGuid(), Now.AddDays(2), "Продлить",
            reservation.Version, actor, Now.AddMinutes(2)));
    }

    [Fact]
    public void ExtensionAndExpirationUseVersionAndDoNotReopenFinalReservation()
    {
        var actor = Guid.NewGuid();
        var reservation = Create(false, 0, actor);
        var extension = Guid.NewGuid();
        Assert.True(reservation.Extend(extension, Now.AddDays(2), "Клиент подтвердил визит", reservation.Version,
            actor, Now.AddMinutes(1)));
        Assert.False(reservation.Extend(extension, Now.AddDays(2), "Клиент подтвердил визит", 1, actor,
            Now.AddMinutes(2)));
        Assert.Throws<DomainException>(() => reservation.Expire(Guid.NewGuid(), reservation.Version, actor,
            Now.AddDays(1)));
        Assert.True(reservation.Expire(Guid.NewGuid(), reservation.Version, actor, Now.AddDays(3)));
        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.Throws<DomainException>(() => reservation.Cancel(Guid.NewGuid(), "Поздняя отмена",
            reservation.Version, actor, Now.AddDays(3)));
    }

    [Fact]
    public void DepositAndCurrencyRulesAreExplicit()
    {
        var actor = Guid.NewGuid();
        Assert.Throws<DomainException>(() => Create(true, 0, actor));
        Assert.Throws<DomainException>(() => Create(false, 1, actor));
        Assert.Throws<DomainException>(() => Reservation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), actor, Guid.NewGuid(), Now.AddDays(31),
            false, 0, "RUB", Now));
    }

    private static Reservation Create(bool depositRequired, decimal amount, Guid actor) => Reservation.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        actor, Guid.NewGuid(), Now.AddDays(1), depositRequired, amount, "RUB", Now);
}
