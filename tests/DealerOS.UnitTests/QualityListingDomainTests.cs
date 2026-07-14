using DealerOS.Modules.Operations.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.UnitTests;

public sealed class QualityListingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BlockingObservation_PreventsPassAndSelectsConcreteRework()
    {
        var executionId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        var quality = QualityCheck.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), executionId, 1,
            Guid.NewGuid(), "{\"version\":1}", Now);
        quality.AddObservation(Guid.NewGuid(), QualityObservationSeverity.Critical, workOrderId, Guid.NewGuid(),
            "Некачественная окраска", true, 1, Now.AddMinutes(1));

        var error = Assert.Throws<DomainException>(() => quality.Pass(Guid.NewGuid(), null, 2, Now.AddMinutes(2)));
        Assert.Equal("quality.blocking_observations", error.Code);
        Assert.Equal([workOrderId], quality.RequireRework(Guid.NewGuid(), "Вернуть в цех", 2,
            Now.AddMinutes(3)));
        Assert.Equal(QualityCheckStatus.ReworkRequired, quality.Status);
        Assert.Throws<DomainException>(() => quality.AddObservation(Guid.NewGuid(), QualityObservationSeverity.Minor,
            null, null, "Позднее замечание", false, 3, Now.AddMinutes(4)));
    }

    [Fact]
    public void QualityRework_ReopensCompletedExecutionWithoutRewritingPriorCosts()
    {
        var execution = CreateCompletedExecution();
        var work = Assert.Single(execution.WorkOrders);
        var actualBefore = execution.ActualTotalAmount;

        execution.ReturnWorkForRework(work.Id, "Повторить окраску", execution.Version, Now.AddHours(4));

        Assert.Equal(ReconditioningExecutionStatus.InProgress, execution.Status);
        Assert.Equal(WorkOrderStatus.ReturnedForRework, work.Status);
        Assert.Null(execution.CompletedAt);
        Assert.Equal(actualBefore, execution.ActualTotalAmount);
    }

    [Fact]
    public void InternalDocument_CannotBecomePublicCover()
    {
        var media = new VehicleMedia(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            VehicleMediaCategory.DocumentsInternal, "private/key.jpg", "document.jpg", "image/jpeg", 100,
            10, Guid.NewGuid(), Now);
        var error = Assert.Throws<DomainException>(() => media.SetCover(true, 1));
        Assert.Equal("media.internal_cover", error.Code);
    }

    [Fact]
    public void ListingSnapshot_IsImmutableAndPublicationRequiresExplicitExport()
    {
        var listing = ListingContent.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Lada", "Vesta",
            2023, 20_000, Guid.NewGuid(), Now);
        listing.Update(Guid.NewGuid(), "Климат", "Один владелец", "Следы эксплуатации раскрыты",
            1_500_000, "RUB", "DealerOS Default", 1, Guid.NewGuid(), 1, Now.AddMinutes(1));
        listing.MarkReady(Guid.NewGuid(), "{\"schemaVersion\":1}", Guid.NewGuid(), 2, Now.AddMinutes(2));

        Assert.Throws<DomainException>(() => listing.Update(Guid.NewGuid(), "Другое", "", "Описание",
            1_600_000, "RUB", "DealerOS Default", 1, Guid.NewGuid(), 3, Now.AddMinutes(3)));
        Assert.Throws<DomainException>(() => listing.Publish(Guid.NewGuid(), "demo", null, null,
            Guid.NewGuid(), Now.AddMinutes(4)));

        var exportId = Guid.NewGuid();
        Assert.True(listing.Export(exportId, "demo", Guid.NewGuid(), Now.AddMinutes(5)));
        Assert.False(listing.Export(exportId, "demo", Guid.NewGuid(), Now.AddMinutes(6)));
        listing.Publish(Guid.NewGuid(), "demo", "EXT-1", "https://example.invalid/EXT-1", Guid.NewGuid(),
            Now.AddMinutes(7));
        Assert.Equal(ChannelPublicationStatus.Published, Assert.Single(listing.Publications).Status);
    }

    private static ReconditioningExecution CreateCompletedExecution()
    {
        var execution = ReconditioningExecution.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), 1000, 1200, "RUB", [new ExecutionSourceWork(Guid.NewGuid(),
                Guid.NewGuid(), "Ремонт", true, "Internal", "Цех", 800, 200, "RUB", 2)], Now);
        var work = execution.WorkOrders.Single();
        execution.Start(Guid.NewGuid(), 1, Now);
        execution.StartWork(work.Id, 2, Now.AddMinutes(1));
        execution.RecordActuals(work.Id, 2, 800, 0, null, null, null, 3, Now.AddHours(1));
        execution.CompleteWork(work.Id, "Готово", 4, Now.AddHours(2));
        execution.Complete(Guid.NewGuid(), 5, Now.AddHours(3));
        return execution;
    }
}
