using DealerOS.Modules.Inspections.Domain;
using DealerOS.Modules.Reconditioning.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.UnitTests;

public sealed class ReconditioningDomainTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid InspectionId = Guid.NewGuid();
    private static readonly Guid AuthorId = Guid.NewGuid();
    private static readonly Guid ManagerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_AutomaticallyAddsEveryRepairRequiredDefectAndCalculatesBudget()
    {
        var required = Defect(true, 32_000m, "RUB");
        var optional = Defect(false, 5_000m, "RUB");

        var plan = CreatePlan(required, optional);

        var work = Assert.Single(plan.Works);
        Assert.Equal(required.Id, work.SourceDefectId);
        Assert.True(work.IsMandatory);
        Assert.Equal(32_000m, work.EstimatedLaborAmount);
        Assert.Empty(plan.Omissions);
    }

    [Fact]
    public void MoneyAndCurrencies_AreValidatedAndMixedCurrencyBlocksSubmission()
    {
        var plan = CreatePlan(Defect(true, 10_000m, "RUB"));
        var source = Defect(false, null, null);
        Assert.Throws<DomainException>(() => plan.AddWork(Guid.NewGuid(), source, "Диагностика", "Проверка",
            ReconditioningWorkCategory.Mechanical, ReconditioningWorkPriority.Normal, false,
            ReconditioningExecutorType.External, "Подрядчик", -1, 0, "RUB", 1, null, plan.Version, Now));

        plan.AddWork(Guid.NewGuid(), source, "Диагностика", "Проверка", ReconditioningWorkCategory.Mechanical,
            ReconditioningWorkPriority.Normal, false, ReconditioningExecutorType.External, "Подрядчик", 100,
            0, "USD", 1, null, plan.Version, Now);
        var error = Assert.Throws<DomainException>(() => plan.Submit(AuthorId, plan.Version, Now));
        Assert.Equal("reconditioning.mixed_currencies", error.Code);
    }

    [Fact]
    public void RemovingMandatoryWork_RequiresExplicitReasonAndKeepsOmission()
    {
        var plan = CreatePlan(Defect(true, 10_000m, "RUB"));
        var work = Assert.Single(plan.Works);

        Assert.Throws<DomainException>(() => plan.RemoveWork(work.Id, null, AuthorId, plan.Version, Now));
        plan.RemoveWork(work.Id, "Ремонт уже выполнен поставщиком", AuthorId, plan.Version, Now);

        var omission = Assert.Single(plan.Omissions);
        Assert.Equal(work.SourceDefectId, omission.SourceDefectId);
        Assert.Equal("Ремонт уже выполнен поставщиком", omission.Reason);
        Assert.Empty(plan.Works);
        Assert.Equal("reconditioning.empty_plan", Assert.Throws<DomainException>(() =>
            plan.Submit(AuthorId, plan.Version, Now)).Code);
    }

    [Fact]
    public void Approval_IsIdempotentAndCreatesImmutableBudgetSnapshot()
    {
        var plan = CreatePlan(Defect(true, 25_000m, "RUB"));
        plan.Submit(AuthorId, plan.Version, Now);
        var decisionId = Guid.NewGuid();

        Assert.True(plan.Approve(decisionId, ManagerId, 24_000m, "RUB", "Лимит скорректирован",
            plan.Version, Now.AddMinutes(1)));
        var approvedVersion = plan.Version;
        Assert.False(plan.Approve(decisionId, ManagerId, 24_000m, "RUB", "Лимит скорректирован",
            1, Now.AddMinutes(2)));

        var snapshot = Assert.Single(plan.BudgetSnapshots);
        Assert.Equal(25_000m, snapshot.PlannedTotalAmount);
        Assert.Equal(24_000m, snapshot.ApprovedLimitAmount);
        Assert.Equal(approvedVersion, plan.Version);
        Assert.Equal(ReconditioningPlanStatus.Approved, plan.Status);
        Assert.Throws<DomainException>(() => plan.UpdateWork(plan.Works.Single().Id, "Изменить", "Нельзя",
            ReconditioningWorkCategory.Body, ReconditioningWorkPriority.Low, true,
            ReconditioningExecutorType.Internal, "Участок", 1, 1, "RUB", 1, null, plan.Version, Now));
    }

    [Fact]
    public void ApprovedPlan_RevisionCopiesCompositionWithoutChangingSource()
    {
        var plan = CreatePlan(Defect(true, 25_000m, "RUB"));
        plan.Submit(AuthorId, plan.Version, Now);
        plan.Approve(Guid.NewGuid(), ManagerId, null, null, null, plan.Version, Now);
        var sourceVersion = plan.Version;

        var revision = ReconditioningPlan.CreateRevision(plan, AuthorId, Now.AddDays(1));

        Assert.Equal(ReconditioningPlanStatus.Approved, plan.Status);
        Assert.Equal(sourceVersion, plan.Version);
        Assert.Equal(ReconditioningPlanStatus.Draft, revision.Status);
        Assert.Equal(2, revision.Revision);
        Assert.Equal(plan.Id, revision.RevisesPlanId);
        Assert.Equal(plan.Works.Single().SourceDefectId, revision.Works.Single().SourceDefectId);
        Assert.NotEqual(plan.Works.Single().Id, revision.Works.Single().Id);
        Assert.Empty(revision.BudgetSnapshots);
        Assert.Empty(revision.Decisions);
    }

    [Fact]
    public void DecisionId_CannotBeReusedForDifferentDecision()
    {
        var plan = CreatePlan(Defect(true, 25_000m, "RUB"));
        plan.Submit(AuthorId, plan.Version, Now);
        var decisionId = Guid.NewGuid();
        plan.RequestChanges(decisionId, ManagerId, "Уточнить исполнителя", plan.Version, Now);

        var error = Assert.Throws<ConflictException>(() => plan.RequestChanges(decisionId, ManagerId,
            "Другая причина", 1, Now));
        Assert.Equal("reconditioning.decision_id_conflict", error.Code);
    }

    [Fact]
    public void ApprovalDecisionId_CannotBeReusedForDifferentBudgetPayload()
    {
        var plan = CreatePlan(Defect(true, 25_000m, "RUB"));
        plan.Submit(AuthorId, plan.Version, Now);
        var decisionId = Guid.NewGuid();
        plan.Approve(decisionId, ManagerId, 24_000m, "RUB", "Согласованный лимит", plan.Version, Now);

        var error = Assert.Throws<ConflictException>(() => plan.Approve(decisionId, ManagerId, 23_000m, "RUB",
            "Другой лимит", 1, Now.AddMinutes(1)));

        Assert.Equal("reconditioning.decision_id_conflict", error.Code);
        Assert.Single(plan.Decisions);
        Assert.Single(plan.BudgetSnapshots);
    }

    private static ReconditioningPlan CreatePlan(params SourceDefectForPlan[] defects) =>
        ReconditioningPlan.Create(OrganizationId, BranchId, VehicleId, InspectionId, AuthorId, defects, "RUB", Now);

    private static SourceDefectForPlan Defect(bool required, decimal? amount, string? currency) => new(
        Guid.NewGuid(), required ? "Трещина бампера" : "Небольшая царапина", "Описание дефекта", "Устранить",
        InspectionCategory.Body, required ? DefectSeverity.Major : DefectSeverity.Minor, amount, currency, required);
}
