using DealerOS.Modules.Operations.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.UnitTests;

public sealed class OperationsDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Execution_UsesApprovedSnapshotAndRequiresCompletedMandatoryWork()
    {
        var execution = CreateExecution();

        execution.Start(Guid.NewGuid(), 1, Now);

        var error = Assert.Throws<DomainException>(() => execution.Complete(Guid.NewGuid(), 2, Now.AddHours(1)));
        Assert.Equal("operations.mandatory_work_incomplete", error.Code);
        Assert.Equal(1000m, execution.PlannedAmount);
        Assert.Equal(1200m, execution.ApprovedLimitAmount);
    }

    [Fact]
    public void ActualCosts_RequireOverrunDecisionAndCreateImmutableCompletion()
    {
        var execution = CreateExecution();
        var work = Assert.Single(execution.WorkOrders);
        execution.Start(Guid.NewGuid(), 1, Now);
        execution.StartWork(work.Id, 2, Now.AddMinutes(1));
        execution.RecordActuals(work.Id, 8, 1000, 500, "Кузовной подрядчик", "INV-1",
            Now.AddDays(7), 3, Now.AddHours(1));
        execution.CompleteWork(work.Id, "Выполнено", 4, Now.AddHours(2));

        var error = Assert.Throws<DomainException>(() =>
            execution.Complete(Guid.NewGuid(), 5, Now.AddHours(3)));
        Assert.Equal("operations.overrun_approval_required", error.Code);

        var decisionId = Guid.NewGuid();
        Assert.True(execution.ApproveOverrun(decisionId, Guid.NewGuid(), 1600, "RUB", "Подтверждён объём",
            5, Now.AddHours(3)));
        Assert.False(execution.ApproveOverrun(decisionId, execution.OverrunDecisions.Single().ActorUserId,
            1600, "RUB", "Подтверждён объём", 6, Now.AddHours(3)));
        execution.Complete(Guid.NewGuid(), 6, Now.AddHours(4));

        Assert.Equal(ReconditioningExecutionStatus.Completed, execution.Status);
        Assert.Equal(1500m, execution.ActualTotalAmount);
        Assert.Equal(500m, execution.VarianceAmount);
    }

    [Fact]
    public void OverrunDecisionId_CannotBeReusedForDifferentPayload()
    {
        var execution = CreateExecution();
        var work = execution.WorkOrders.Single();
        execution.Start(Guid.NewGuid(), 1, Now);
        execution.StartWork(work.Id, 2, Now.AddMinutes(1));
        execution.RecordActuals(work.Id, 1, 1300, 0, null, null, null, 3, Now.AddMinutes(2));
        var decisionId = Guid.NewGuid();
        var manager = Guid.NewGuid();
        execution.ApproveOverrun(decisionId, manager, 1400, "RUB", "Лимит", 4, Now.AddMinutes(3));

        var error = Assert.Throws<ConflictException>(() => execution.ApproveOverrun(decisionId, manager,
            1500, "RUB", "Другой лимит", 5, Now.AddMinutes(4)));
        Assert.Equal("operations.overrun_decision_id_conflict", error.Code);
        Assert.Single(execution.OverrunDecisions);
    }

    [Fact]
    public void Materials_AreIdempotentAndReturnCannotExceedConsumption()
    {
        var execution = CreateExecution();
        var work = execution.WorkOrders.Single();
        execution.Start(Guid.NewGuid(), 1, Now);
        execution.StartWork(work.Id, 2, Now.AddMinutes(1));
        var movementId = Guid.NewGuid();
        execution.AddMaterial(work.Id, movementId, MaterialMovementType.Consumed, "Краска", 2, "л", 500,
            "RUB", "Поставщик", Guid.NewGuid(), 3, Now.AddMinutes(2));
        execution.AddMaterial(work.Id, movementId, MaterialMovementType.Consumed, "Краска", 2, "л", 500,
            "RUB", "Поставщик", Guid.NewGuid(), 4, Now.AddMinutes(3));

        var error = Assert.Throws<DomainException>(() => execution.AddMaterial(work.Id, Guid.NewGuid(),
            MaterialMovementType.Returned, "Краска", 3, "л", 500, "RUB", "Поставщик", Guid.NewGuid(),
            4, Now.AddMinutes(4)));

        Assert.Equal("operations.material_return_exceeds_consumed", error.Code);
        Assert.Single(work.MaterialMovements);
        Assert.Equal(1000m, execution.ActualMaterialAmount);
    }

    [Fact]
    public void DeadlineNotifications_AreIdempotent()
    {
        var execution = CreateExecution();
        var work = execution.WorkOrders.Single();
        execution.ScheduleWork(work.Id, "Мастер", Now.AddHours(2), 1, Now);

        Assert.Equal(1, execution.GenerateDeadlineNotifications(Now.AddHours(1), TimeSpan.FromDays(1)));
        Assert.Equal(0, execution.GenerateDeadlineNotifications(Now.AddHours(1), TimeSpan.FromDays(1)));
        Assert.Single(execution.Notifications);
    }

    [Fact]
    public void Execution_RejectsMixedCurrencySource()
    {
        var error = Assert.Throws<DomainException>(() => ReconditioningExecution.Create(Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000, 1000, "RUB",
            [Source(currency: "USD")], Now));
        Assert.Equal("operations.execution_currency_mismatch", error.Code);
    }

    private static ReconditioningExecution CreateExecution() => ReconditioningExecution.Create(Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000, 1200, "RUB",
        [Source()], Now);

    private static ExecutionSourceWork Source(string currency = "RUB") => new(Guid.NewGuid(), Guid.NewGuid(),
        "Ремонт бампера", true, "Internal", "Кузовной участок", 800, 200, currency, 2);
}
