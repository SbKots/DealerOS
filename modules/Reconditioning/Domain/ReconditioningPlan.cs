using DealerOS.Modules.Inspections.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Reconditioning.Domain;

public sealed record SourceDefectForPlan(Guid Id, string Title, string Description, string? Recommendation,
    InspectionCategory Category, DefectSeverity Severity, decimal? EstimatedRepairAmount, string? Currency,
    bool RepairRequired);

public sealed class ReconditioningPlan
{
    private readonly List<ReconditioningWork> _works = [];
    private readonly List<ReconditioningDefectOmission> _omissions = [];
    private readonly List<ReconditioningDecision> _decisions = [];
    private readonly List<ReconditioningBudgetSnapshot> _budgetSnapshots = [];
    private readonly List<ReconditioningPlanHistory> _history = [];

    private ReconditioningPlan() { }

    private ReconditioningPlan(Guid id, Guid organizationId, Guid branchId, Guid vehicleId, Guid sourceInspectionId,
        Guid createdByUserId, int revision, Guid? revisesPlanId, IEnumerable<SourceDefectForPlan> sourceDefects,
        string defaultCurrency, DateTimeOffset now)
    {
        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        VehicleId = vehicleId;
        SourceInspectionId = sourceInspectionId;
        CreatedByUserId = createdByUserId;
        Revision = revision;
        RevisesPlanId = revisesPlanId;
        Status = ReconditioningPlanStatus.Draft;
        Version = 1;
        CreatedAt = now;
        UpdatedAt = now;
        _history.Add(new ReconditioningPlanHistory(Guid.NewGuid(), organizationId, id, null, Status,
            createdByUserId, null, null, now));

        foreach (var defect in sourceDefects.Where(x => x.RepairRequired))
        {
            var money = new Money(defect.EstimatedRepairAmount ?? 0, defect.Currency ?? defaultCurrency);
            _works.Add(ReconditioningWork.Create(Guid.NewGuid(), organizationId, id, defect.Id, defect.Title,
                defect.Description, defect.Severity.ToString(), $"Устранить: {defect.Title}",
                defect.Recommendation ?? defect.Description, MapCategory(defect.Category),
                defect.Severity switch
                {
                    DefectSeverity.Critical => ReconditioningWorkPriority.Critical,
                    DefectSeverity.Major => ReconditioningWorkPriority.High,
                    _ => ReconditioningWorkPriority.Normal
                }, true, ReconditioningExecutorType.Internal, "Сервисный участок", money.Amount, 0,
                money.Currency, 1, null, now));
        }
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid SourceInspectionId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public ReconditioningPlanStatus Status { get; private set; }
    public int Revision { get; private set; }
    public Guid? RevisesPlanId { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<ReconditioningWork> Works => _works;
    public IReadOnlyCollection<ReconditioningDefectOmission> Omissions => _omissions;
    public IReadOnlyCollection<ReconditioningDecision> Decisions => _decisions;
    public IReadOnlyCollection<ReconditioningBudgetSnapshot> BudgetSnapshots => _budgetSnapshots;
    public IReadOnlyCollection<ReconditioningPlanHistory> History => _history;

    public static ReconditioningPlan Create(Guid organizationId, Guid branchId, Guid vehicleId,
        Guid sourceInspectionId, Guid createdByUserId, IEnumerable<SourceDefectForPlan> sourceDefects,
        string defaultCurrency, DateTimeOffset now, int revision = 1, Guid? revisesPlanId = null) =>
        new(Guid.NewGuid(), organizationId, branchId, vehicleId, sourceInspectionId, createdByUserId, revision,
            revisesPlanId, sourceDefects, defaultCurrency, now);

    public static ReconditioningPlan CreateRevision(ReconditioningPlan source, Guid createdByUserId,
        DateTimeOffset now)
    {
        if (source.Status != ReconditioningPlanStatus.Approved)
            throw new DomainException("reconditioning.revision_requires_approved",
                "Новую ревизию можно создать только из утверждённого плана.");

        var revision = new ReconditioningPlan
        {
            Id = Guid.NewGuid(),
            OrganizationId = source.OrganizationId,
            BranchId = source.BranchId,
            VehicleId = source.VehicleId,
            SourceInspectionId = source.SourceInspectionId,
            CreatedByUserId = createdByUserId,
            Status = ReconditioningPlanStatus.Draft,
            Revision = source.Revision + 1,
            RevisesPlanId = source.Id,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        foreach (var work in source.Works) revision._works.Add(work.CopyTo(revision.Id, now));
        foreach (var omission in source.Omissions) revision._omissions.Add(omission.CopyTo(revision.Id, createdByUserId, now));
        revision._history.Add(new ReconditioningPlanHistory(Guid.NewGuid(), source.OrganizationId, revision.Id,
            null, ReconditioningPlanStatus.Draft, createdByUserId, "Создана новая ревизия утверждённого плана.",
            null, now));
        return revision;
    }

    public ReconditioningWork AddWork(Guid workId, SourceDefectForPlan sourceDefect, string? title,
        string? description, ReconditioningWorkCategory category, ReconditioningWorkPriority priority,
        bool isMandatory, ReconditioningExecutorType executorType, string? executorName,
        decimal estimatedLaborAmount, decimal estimatedPartsAmount, string? currency, int estimatedDurationDays,
        string? comment, long expectedVersion, DateTimeOffset now)
    {
        EnsureEditable(expectedVersion);
        var existing = _works.SingleOrDefault(x => x.Id == workId);
        if (existing is not null) return existing;
        var work = ReconditioningWork.Create(workId, OrganizationId, Id, sourceDefect.Id, sourceDefect.Title,
            sourceDefect.Description, sourceDefect.Severity.ToString(), title, description, category, priority,
            sourceDefect.RepairRequired || isMandatory, executorType, executorName, estimatedLaborAmount,
            estimatedPartsAmount, currency, estimatedDurationDays, comment, now);
        _works.Add(work);
        _omissions.RemoveAll(x => x.SourceDefectId == sourceDefect.Id);
        Touch(now);
        return work;
    }

    public ReconditioningWork UpdateWork(Guid workId, string? title, string? description,
        ReconditioningWorkCategory category, ReconditioningWorkPriority priority, bool isMandatory,
        ReconditioningExecutorType executorType, string? executorName, decimal estimatedLaborAmount,
        decimal estimatedPartsAmount, string? currency, int estimatedDurationDays, string? comment,
        long expectedVersion, DateTimeOffset now)
    {
        EnsureEditable(expectedVersion);
        var work = _works.SingleOrDefault(x => x.Id == workId)
            ?? throw new NotFoundException("Работа плана не найдена.");
        work.Update(title, description, category, priority, work.IsMandatory || isMandatory, executorType,
            executorName, estimatedLaborAmount, estimatedPartsAmount, currency, estimatedDurationDays, comment, now);
        Touch(now);
        return work;
    }

    public ReconditioningWork RemoveWork(Guid workId, string? omissionReason, Guid actorUserId,
        long expectedVersion, DateTimeOffset now)
    {
        EnsureEditable(expectedVersion);
        var work = _works.SingleOrDefault(x => x.Id == workId)
            ?? throw new NotFoundException("Работа плана не найдена.");
        var removesLastMandatoryWork = work.IsMandatory
            && _works.All(x => x.Id == work.Id || x.SourceDefectId != work.SourceDefectId);
        var reason = removesLastMandatoryWork
            ? RequireText(omissionReason, 2000, "Причина исключения обязательного дефекта обязательна.")
            : null;
        _works.Remove(work);
        if (removesLastMandatoryWork)
        {
            _omissions.RemoveAll(x => x.SourceDefectId == work.SourceDefectId);
            _omissions.Add(new ReconditioningDefectOmission(Guid.NewGuid(), OrganizationId, Id,
                work.SourceDefectId, work.SourceDefectTitle, reason!, actorUserId, now));
        }
        Touch(now);
        return work;
    }

    public void Submit(Guid actorUserId, long expectedVersion, DateTimeOffset now)
    {
        EnsureEditable(expectedVersion);
        if (_works.Count == 0)
            throw new DomainException("reconditioning.empty_plan", "Нельзя отправить пустой план на согласование.");
        var currencies = _works.Select(x => x.Currency).Distinct(StringComparer.Ordinal).ToArray();
        if (currencies.Length != 1)
            throw new DomainException("reconditioning.mixed_currencies",
                "Перед отправкой приведите все работы плана к одной валюте.");
        TransitionTo(ReconditioningPlanStatus.Submitted, actorUserId, null, null, now);
        SubmittedAt = now;
    }

    public bool Approve(Guid decisionId, Guid actorUserId, decimal? approvedLimitAmount, string? currency,
        string? comment, long expectedVersion, DateTimeOffset now)
    {
        if (MatchesDecisionCommand(decisionId, ReconditioningDecisionType.Approved, actorUserId, comment,
            approvedLimitAmount, currency)) return false;
        var normalizedComment = NormalizeOptional(comment, 2000);
        EnsureSubmitted(expectedVersion);
        var totals = GetSingleCurrencyTotals();
        var limit = new Money(approvedLimitAmount ?? totals.Total, currency ?? totals.Currency);
        if (limit.Currency != totals.Currency)
            throw new DomainException("reconditioning.approval_currency_mismatch",
                "Лимит согласования должен быть в валюте плана.");
        var decision = new ReconditioningDecision(decisionId, OrganizationId, Id,
            ReconditioningDecisionType.Approved, actorUserId, normalizedComment, limit.Amount, limit.Currency, now);
        _decisions.Add(decision);
        _budgetSnapshots.Add(new ReconditioningBudgetSnapshot(Guid.NewGuid(), OrganizationId, Id,
            totals.Labor, totals.Parts, totals.Total, limit.Amount, totals.Currency, actorUserId, now));
        TransitionTo(ReconditioningPlanStatus.Approved, actorUserId, normalizedComment, decisionId, now);
        DecidedAt = now;
        return true;
    }

    public bool Reject(Guid decisionId, Guid actorUserId, string? reason, long expectedVersion, DateTimeOffset now)
    {
        if (MatchesDecisionCommand(decisionId, ReconditioningDecisionType.Rejected, actorUserId, reason,
            null, null)) return false;
        var normalizedReason = RequireText(reason, 2000, "Причина отклонения обязательна.");
        EnsureSubmitted(expectedVersion);
        _decisions.Add(new ReconditioningDecision(decisionId, OrganizationId, Id,
            ReconditioningDecisionType.Rejected, actorUserId, normalizedReason, null, null, now));
        TransitionTo(ReconditioningPlanStatus.Rejected, actorUserId, normalizedReason, decisionId, now);
        DecidedAt = now;
        return true;
    }

    public bool RequestChanges(Guid decisionId, Guid actorUserId, string? reason, long expectedVersion,
        DateTimeOffset now)
    {
        if (MatchesDecisionCommand(decisionId, ReconditioningDecisionType.ChangesRequested, actorUserId, reason,
            null, null)) return false;
        var normalizedReason = RequireText(reason, 2000, "Причина возврата на доработку обязательна.");
        EnsureSubmitted(expectedVersion);
        _decisions.Add(new ReconditioningDecision(decisionId, OrganizationId, Id,
            ReconditioningDecisionType.ChangesRequested, actorUserId, normalizedReason, null, null, now));
        TransitionTo(ReconditioningPlanStatus.ChangesRequested, actorUserId, normalizedReason, decisionId, now);
        return true;
    }

    public void Cancel(Guid actorUserId, string? reason, long expectedVersion, DateTimeOffset now)
    {
        if (Status == ReconditioningPlanStatus.Cancelled) return;
        EnsureVersion(expectedVersion);
        if (Status is ReconditioningPlanStatus.Approved or ReconditioningPlanStatus.Rejected)
            throw new DomainException("reconditioning.terminal_immutable", "Завершённый план нельзя отменить.");
        var normalizedReason = RequireText(reason, 2000, "Причина отмены обязательна.");
        TransitionTo(ReconditioningPlanStatus.Cancelled, actorUserId, normalizedReason, null, now);
        CancelledAt = now;
    }

    public ReconditioningDecision? FindDecision(Guid decisionId) => _decisions.SingleOrDefault(x => x.Id == decisionId);

    public bool MatchesDecisionCommand(Guid decisionId, ReconditioningDecisionType type, Guid actorUserId,
        string? reason, decimal? approvedLimitAmount, string? currency)
    {
        var existing = FindDecision(decisionId);
        if (existing is null) return false;

        var normalizedReason = type switch
        {
            ReconditioningDecisionType.Approved => NormalizeOptional(reason, 2000),
            ReconditioningDecisionType.Rejected => RequireText(reason, 2000, "Причина отклонения обязательна."),
            ReconditioningDecisionType.ChangesRequested => RequireText(reason, 2000,
                "Причина возврата на доработку обязательна."),
            _ => throw new DomainException("reconditioning.invalid_decision", "Недопустимое решение.")
        };
        decimal? normalizedAmount = null;
        string? normalizedCurrency = null;
        if (type == ReconditioningDecisionType.Approved)
        {
            var totals = GetSingleCurrencyTotals();
            var limit = new Money(approvedLimitAmount ?? totals.Total, currency ?? totals.Currency);
            if (limit.Currency != totals.Currency)
                throw new DomainException("reconditioning.approval_currency_mismatch",
                    "Лимит согласования должен быть в валюте плана.");
            normalizedAmount = limit.Amount;
            normalizedCurrency = limit.Currency;
        }

        if (existing.Type != type || existing.ActorUserId != actorUserId || existing.Reason != normalizedReason
            || existing.ApprovedLimitAmount != normalizedAmount || existing.Currency != normalizedCurrency)
            throw new ConflictException("reconditioning.decision_id_conflict",
                "Идентификатор решения уже использован для другой команды.");
        return true;
    }

    private (decimal Labor, decimal Parts, decimal Total, string Currency) GetSingleCurrencyTotals()
    {
        var currencies = _works.Select(x => x.Currency).Distinct(StringComparer.Ordinal).ToArray();
        if (_works.Count == 0 || currencies.Length != 1)
            throw new DomainException("reconditioning.mixed_currencies", "План должен содержать работы в одной валюте.");
        var labor = _works.Sum(x => x.EstimatedLaborAmount);
        var parts = _works.Sum(x => x.EstimatedPartsAmount);
        return (labor, parts, labor + parts, currencies[0]);
    }

    private void EnsureEditable(long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status is not (ReconditioningPlanStatus.Draft or ReconditioningPlanStatus.ChangesRequested))
            throw new DomainException("reconditioning.not_editable",
                "Редактировать можно только черновик или план, возвращённый на доработку.");
    }

    private void EnsureSubmitted(long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status != ReconditioningPlanStatus.Submitted)
            throw new DomainException("reconditioning.not_submitted", "Решение можно принять только по отправленному плану.");
    }

    private void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion)
            throw new ConflictException("reconditioning.version_conflict",
                "План был изменён другим пользователем. Обновите данные.");
    }

    private void TransitionTo(ReconditioningPlanStatus status, Guid actorUserId, string? reason,
        Guid? decisionId, DateTimeOffset now)
    {
        var previous = Status;
        Status = status;
        _history.Add(new ReconditioningPlanHistory(Guid.NewGuid(), OrganizationId, Id, previous, status,
            actorUserId, reason, decisionId, now));
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    private static string RequireText(string? value, int maxLength, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("reconditioning.required_field", message);
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new DomainException("reconditioning.field_too_long", $"Поле не должно превышать {maxLength} символов.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength) => string.IsNullOrWhiteSpace(value)
        ? null
        : RequireText(value, maxLength, string.Empty);

    private static ReconditioningWorkCategory MapCategory(InspectionCategory category) => category switch
    {
        InspectionCategory.Body => ReconditioningWorkCategory.Body,
        InspectionCategory.Interior => ReconditioningWorkCategory.Interior,
        InspectionCategory.Electrical => ReconditioningWorkCategory.Electrical,
        InspectionCategory.WheelsAndTires => ReconditioningWorkCategory.WheelsAndTires,
        InspectionCategory.DocumentsAndEquipment => ReconditioningWorkCategory.Documents,
        InspectionCategory.Engine or InspectionCategory.Transmission or InspectionCategory.Suspension
            or InspectionCategory.Brakes or InspectionCategory.Steering => ReconditioningWorkCategory.Mechanical,
        _ => ReconditioningWorkCategory.Other
    };
}

public sealed class ReconditioningWork
{
    private ReconditioningWork() { }

    private ReconditioningWork(Guid id, Guid organizationId, Guid planId, Guid sourceDefectId,
        string sourceDefectTitle, string sourceDefectDescription, string sourceDefectSeverity, string title,
        string description, ReconditioningWorkCategory category, ReconditioningWorkPriority priority,
        bool isMandatory, ReconditioningExecutorType executorType, string executorName,
        decimal estimatedLaborAmount, decimal estimatedPartsAmount, string currency, int estimatedDurationDays,
        string? comment, DateTimeOffset now)
    {
        Id = id;
        OrganizationId = organizationId;
        PlanId = planId;
        SourceDefectId = sourceDefectId;
        SourceDefectTitle = sourceDefectTitle;
        SourceDefectDescription = sourceDefectDescription;
        SourceDefectSeverity = sourceDefectSeverity;
        Apply(title, description, category, priority, isMandatory, executorType, executorName,
            estimatedLaborAmount, estimatedPartsAmount, currency, estimatedDurationDays, comment);
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid SourceDefectId { get; private set; }
    public string SourceDefectTitle { get; private set; } = string.Empty;
    public string SourceDefectDescription { get; private set; } = string.Empty;
    public string SourceDefectSeverity { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public ReconditioningWorkCategory Category { get; private set; }
    public ReconditioningWorkPriority Priority { get; private set; }
    public bool IsMandatory { get; private set; }
    public ReconditioningExecutorType ExecutorType { get; private set; }
    public string ExecutorName { get; private set; } = string.Empty;
    public decimal EstimatedLaborAmount { get; private set; }
    public decimal EstimatedPartsAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public int EstimatedDurationDays { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    internal static ReconditioningWork Create(Guid id, Guid organizationId, Guid planId, Guid sourceDefectId,
        string sourceDefectTitle, string sourceDefectDescription, string sourceDefectSeverity, string? title,
        string? description, ReconditioningWorkCategory category, ReconditioningWorkPriority priority,
        bool isMandatory, ReconditioningExecutorType executorType, string? executorName,
        decimal estimatedLaborAmount, decimal estimatedPartsAmount, string? currency, int estimatedDurationDays,
        string? comment, DateTimeOffset now)
    {
        if (id == Guid.Empty || sourceDefectId == Guid.Empty)
            throw new DomainException("reconditioning.id_required", "Идентификаторы работы и дефекта обязательны.");
        return new ReconditioningWork(id, organizationId, planId, sourceDefectId,
            RequireText(sourceDefectTitle, 300), RequireText(sourceDefectDescription, 4000),
            RequireText(sourceDefectSeverity, 30), RequireText(title, 300), RequireText(description, 4000),
            category, priority, isMandatory, executorType, RequireText(executorName, 300),
            estimatedLaborAmount, estimatedPartsAmount, currency ?? string.Empty, estimatedDurationDays,
            NormalizeOptional(comment, 2000), now);
    }

    internal void Update(string? title, string? description, ReconditioningWorkCategory category,
        ReconditioningWorkPriority priority, bool isMandatory, ReconditioningExecutorType executorType,
        string? executorName, decimal estimatedLaborAmount, decimal estimatedPartsAmount, string? currency,
        int estimatedDurationDays, string? comment, DateTimeOffset now)
    {
        Apply(RequireText(title, 300), RequireText(description, 4000), category, priority, isMandatory,
            executorType, RequireText(executorName, 300), estimatedLaborAmount, estimatedPartsAmount,
            currency ?? string.Empty, estimatedDurationDays, NormalizeOptional(comment, 2000));
        UpdatedAt = now;
    }

    internal ReconditioningWork CopyTo(Guid planId, DateTimeOffset now) => new(Guid.NewGuid(), OrganizationId,
        planId, SourceDefectId, SourceDefectTitle, SourceDefectDescription, SourceDefectSeverity, Title,
        Description, Category, Priority, IsMandatory, ExecutorType, ExecutorName, EstimatedLaborAmount,
        EstimatedPartsAmount, Currency, EstimatedDurationDays, Comment, now);

    private void Apply(string title, string description, ReconditioningWorkCategory category,
        ReconditioningWorkPriority priority, bool isMandatory, ReconditioningExecutorType executorType,
        string executorName, decimal estimatedLaborAmount, decimal estimatedPartsAmount, string currency,
        int estimatedDurationDays, string? comment)
    {
        if (!Enum.IsDefined(category) || !Enum.IsDefined(priority) || !Enum.IsDefined(executorType))
            throw new DomainException("reconditioning.invalid_classification", "Классификация работы недопустима.");
        if (estimatedDurationDays is < 1 or > 365)
            throw new DomainException("reconditioning.invalid_duration", "Срок работы должен быть от 1 до 365 дней.");
        var labor = new Money(estimatedLaborAmount, currency);
        var parts = new Money(estimatedPartsAmount, currency);
        Title = title;
        Description = description;
        Category = category;
        Priority = priority;
        IsMandatory = isMandatory;
        ExecutorType = executorType;
        ExecutorName = executorName;
        EstimatedLaborAmount = labor.Amount;
        EstimatedPartsAmount = parts.Amount;
        Currency = labor.Currency;
        EstimatedDurationDays = estimatedDurationDays;
        Comment = comment;
    }

    private static string RequireText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("reconditioning.required_field", "Обязательное поле работы не заполнено.");
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new DomainException("reconditioning.field_too_long", $"Поле не должно превышать {maxLength} символов.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength) => string.IsNullOrWhiteSpace(value)
        ? null
        : RequireText(value, maxLength);
}

public sealed class ReconditioningDefectOmission
{
    private ReconditioningDefectOmission() { }
    internal ReconditioningDefectOmission(Guid id, Guid organizationId, Guid planId, Guid sourceDefectId,
        string sourceDefectTitle, string reason, Guid decidedByUserId, DateTimeOffset decidedAt) =>
        (Id, OrganizationId, PlanId, SourceDefectId, SourceDefectTitle, Reason, DecidedByUserId, DecidedAt) =
        (id, organizationId, planId, sourceDefectId, sourceDefectTitle, reason, decidedByUserId, decidedAt);
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid SourceDefectId { get; private set; }
    public string SourceDefectTitle { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public Guid DecidedByUserId { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }
    internal ReconditioningDefectOmission CopyTo(Guid planId, Guid actorUserId, DateTimeOffset now) =>
        new(Guid.NewGuid(), OrganizationId, planId, SourceDefectId, SourceDefectTitle, Reason, actorUserId, now);
}

public sealed class ReconditioningDecision
{
    private ReconditioningDecision() { }
    internal ReconditioningDecision(Guid id, Guid organizationId, Guid planId, ReconditioningDecisionType type,
        Guid actorUserId, string? reason, decimal? approvedLimitAmount, string? currency, DateTimeOffset decidedAt) =>
        (Id, OrganizationId, PlanId, Type, ActorUserId, Reason, ApprovedLimitAmount, Currency, DecidedAt) =
        (id, organizationId, planId, type, actorUserId, reason, approvedLimitAmount, currency, decidedAt);
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PlanId { get; private set; }
    public ReconditioningDecisionType Type { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string? Reason { get; private set; }
    public decimal? ApprovedLimitAmount { get; private set; }
    public string? Currency { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }
}

public sealed class ReconditioningBudgetSnapshot
{
    private ReconditioningBudgetSnapshot() { }
    internal ReconditioningBudgetSnapshot(Guid id, Guid organizationId, Guid planId, decimal laborAmount,
        decimal partsAmount, decimal plannedTotalAmount, decimal approvedLimitAmount, string currency,
        Guid approvedByUserId, DateTimeOffset approvedAt) =>
        (Id, OrganizationId, PlanId, LaborAmount, PartsAmount, PlannedTotalAmount, ApprovedLimitAmount,
            Currency, ApprovedByUserId, ApprovedAt) =
        (id, organizationId, planId, laborAmount, partsAmount, plannedTotalAmount, approvedLimitAmount,
            currency, approvedByUserId, approvedAt);
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PlanId { get; private set; }
    public decimal LaborAmount { get; private set; }
    public decimal PartsAmount { get; private set; }
    public decimal PlannedTotalAmount { get; private set; }
    public decimal ApprovedLimitAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public Guid ApprovedByUserId { get; private set; }
    public DateTimeOffset ApprovedAt { get; private set; }
}

public sealed class ReconditioningPlanHistory
{
    private ReconditioningPlanHistory() { }
    internal ReconditioningPlanHistory(Guid id, Guid organizationId, Guid planId,
        ReconditioningPlanStatus? fromStatus, ReconditioningPlanStatus toStatus, Guid actorUserId, string? reason,
        Guid? decisionId, DateTimeOffset occurredAt) =>
        (Id, OrganizationId, PlanId, FromStatus, ToStatus, ActorUserId, Reason, DecisionId, OccurredAt) =
        (id, organizationId, planId, fromStatus, toStatus, actorUserId, reason, decisionId, occurredAt);
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PlanId { get; private set; }
    public ReconditioningPlanStatus? FromStatus { get; private set; }
    public ReconditioningPlanStatus ToStatus { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string? Reason { get; private set; }
    public Guid? DecisionId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
