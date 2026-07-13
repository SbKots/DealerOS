using DealerOS.SharedKernel;

namespace DealerOS.Modules.Operations.Domain;

public sealed record ExecutionSourceWork(Guid PlanWorkId, Guid SourceDefectId, string Title, bool IsMandatory,
    string ExecutorType, string ExecutorName, decimal PlannedLaborAmount, decimal PlannedPartsAmount,
    string Currency, int EstimatedDurationDays);

public sealed class ReconditioningExecution
{
    private readonly List<ExecutionWorkOrder> _workOrders = [];
    private readonly List<ExecutionOverrunDecision> _overrunDecisions = [];
    private readonly List<ExecutionNotification> _notifications = [];

    private ReconditioningExecution() { }

    private ReconditioningExecution(Guid id, Guid organizationId, Guid branchId, Guid vehicleId, Guid planId,
        Guid budgetSnapshotId, Guid createdByUserId, decimal plannedAmount, decimal approvedLimitAmount,
        string currency, IEnumerable<ExecutionSourceWork> works, DateTimeOffset now)
    {
        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        VehicleId = vehicleId;
        PlanId = planId;
        BudgetSnapshotId = budgetSnapshotId;
        CreatedByUserId = createdByUserId;
        PlannedAmount = new Money(plannedAmount, currency).Amount;
        ApprovedLimitAmount = new Money(approvedLimitAmount, currency).Amount;
        Currency = new Money(0, currency).Currency;
        Status = ReconditioningExecutionStatus.Draft;
        Version = 1;
        CreatedAt = now;
        UpdatedAt = now;

        foreach (var work in works)
        {
            if (!string.Equals(work.Currency, Currency, StringComparison.OrdinalIgnoreCase))
                throw new DomainException("operations.execution_currency_mismatch",
                    "Все работы execution должны использовать валюту утверждённого snapshot.");
            _workOrders.Add(ExecutionWorkOrder.Create(Guid.NewGuid(), organizationId, id, work, now));
        }

        if (_workOrders.Count == 0)
            throw new DomainException("operations.execution_empty", "Нельзя создать execution без работ.");
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid BudgetSnapshotId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public ReconditioningExecutionStatus Status { get; private set; }
    public decimal PlannedAmount { get; private set; }
    public decimal ApprovedLimitAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<ExecutionWorkOrder> WorkOrders => _workOrders;
    public IReadOnlyCollection<ExecutionOverrunDecision> OverrunDecisions => _overrunDecisions;
    public IReadOnlyCollection<ExecutionNotification> Notifications => _notifications;
    public decimal ActualLaborAmount => _workOrders.Sum(x => x.ActualLaborAmount);
    public decimal ActualMaterialAmount => _workOrders.Sum(x => x.MaterialAmount);
    public decimal ActualExternalAmount => _workOrders.Sum(x => x.ActualExternalAmount);
    public decimal ActualTotalAmount => ActualLaborAmount + ActualMaterialAmount + ActualExternalAmount;
    public decimal VarianceAmount => ActualTotalAmount - PlannedAmount;

    public static ReconditioningExecution Create(Guid organizationId, Guid branchId, Guid vehicleId, Guid planId,
        Guid budgetSnapshotId, Guid createdByUserId, decimal plannedAmount, decimal approvedLimitAmount,
        string currency, IEnumerable<ExecutionSourceWork> works, DateTimeOffset now) =>
        new(Guid.NewGuid(), organizationId, branchId, vehicleId, planId, budgetSnapshotId, createdByUserId,
            plannedAmount, approvedLimitAmount, currency, works, now);

    public void Start(Guid actorUserId, long expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (Status != ReconditioningExecutionStatus.Draft)
            throw new DomainException("operations.execution_not_draft", "Начать можно только черновик execution.");
        Status = ReconditioningExecutionStatus.InProgress;
        StartedAt = now;
        Touch(now);
    }

    public void ScheduleWork(Guid workOrderId, string? assigneeName, DateTimeOffset dueAt, long expectedVersion,
        DateTimeOffset now)
    {
        EnsureMutable(expectedVersion);
        FindWork(workOrderId).Schedule(assigneeName, dueAt, now);
        Touch(now);
    }

    public void StartWork(Guid workOrderId, long expectedVersion, DateTimeOffset now)
    {
        EnsureInProgress(expectedVersion);
        FindWork(workOrderId).Start(now);
        Touch(now);
    }

    public void RecordActuals(Guid workOrderId, decimal laborHours, decimal laborAmount,
        decimal externalAmount, string? contractorName, string? invoiceReference, DateTimeOffset? contractorDueAt,
        long expectedVersion, DateTimeOffset now)
    {
        EnsureInProgress(expectedVersion);
        FindWork(workOrderId).RecordActuals(laborHours, laborAmount, externalAmount, Currency, contractorName,
            invoiceReference, contractorDueAt, now);
        Touch(now);
    }

    public bool AddMaterial(Guid workOrderId, Guid movementId, MaterialMovementType type, string? name,
        decimal quantity, string? unit, decimal unitCost, string? currency, string? supplierName,
        Guid actorUserId, long expectedVersion, DateTimeOffset now)
    {
        EnsureInProgress(expectedVersion);
        var changed = FindWork(workOrderId).AddMaterial(movementId, type, name, quantity, unit, unitCost, currency,
            supplierName, actorUserId, now);
        if (changed) Touch(now);
        return changed;
    }

    public void BlockWork(Guid workOrderId, string? reason, long expectedVersion, DateTimeOffset now)
    {
        EnsureInProgress(expectedVersion);
        FindWork(workOrderId).Block(reason, now);
        Status = ReconditioningExecutionStatus.Blocked;
        Touch(now);
    }

    public void ResumeWork(Guid workOrderId, long expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (Status is not (ReconditioningExecutionStatus.InProgress or ReconditioningExecutionStatus.Blocked))
            throw new DomainException("operations.execution_not_active", "Execution не находится в работе.");
        FindWork(workOrderId).Resume(now);
        Status = _workOrders.Any(x => x.Status == WorkOrderStatus.Blocked)
            ? ReconditioningExecutionStatus.Blocked
            : ReconditioningExecutionStatus.InProgress;
        Touch(now);
    }

    public void CompleteWork(Guid workOrderId, string? comment, long expectedVersion, DateTimeOffset now)
    {
        EnsureInProgress(expectedVersion, allowBlockedExecution: true);
        FindWork(workOrderId).Complete(comment, now);
        Status = _workOrders.Any(x => x.Status == WorkOrderStatus.Blocked)
            ? ReconditioningExecutionStatus.Blocked
            : ReconditioningExecutionStatus.InProgress;
        Touch(now);
    }

    public void ReturnWorkForRework(Guid workOrderId, string? reason, long expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (Status != ReconditioningExecutionStatus.Completed
            && !(Status == ReconditioningExecutionStatus.InProgress
                 && _workOrders.Any(x => x.Status == WorkOrderStatus.ReturnedForRework)))
            throw new DomainException("operations.qc_rework_requires_completed",
                "Возврат из контроля качества доступен только для завершённого execution.");
        FindWork(workOrderId).ReturnForRework(reason, now);
        Status = ReconditioningExecutionStatus.InProgress;
        CompletedAt = null;
        Touch(now);
    }

    public void SetSettlement(Guid workOrderId, ContractorSettlementStatus status, string? comment,
        Guid actorUserId, long expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (Status == ReconditioningExecutionStatus.Cancelled)
            throw new DomainException("operations.execution_cancelled", "Отменённый execution нельзя изменять.");
        FindWork(workOrderId).SetSettlement(status, comment, actorUserId, now);
        Touch(now);
    }

    public bool ApproveOverrun(Guid decisionId, Guid actorUserId, decimal approvedLimitAmount, string? currency,
        string? reason, long expectedVersion, DateTimeOffset now)
    {
        var limit = new Money(approvedLimitAmount, currency);
        var normalizedReason = RequireText(reason, 2000, "Причина решения по перерасходу обязательна.");
        var existing = _overrunDecisions.SingleOrDefault(x => x.Id == decisionId);
        if (existing is not null)
        {
            if (existing.ActorUserId != actorUserId || existing.ApprovedLimitAmount != limit.Amount
                || existing.Currency != limit.Currency || existing.Reason != normalizedReason)
                throw new ConflictException("operations.overrun_decision_id_conflict",
                    "Decision ID уже использован для другой команды.");
            return false;
        }

        EnsureVersion(expectedVersion);
        if (limit.Currency != Currency)
            throw new DomainException("operations.overrun_currency_mismatch", "Лимит перерасхода должен быть в валюте execution.");
        if (ActualTotalAmount <= ApprovedLimitAmount)
            throw new DomainException("operations.no_overrun", "Фактический расход не превышает утверждённый лимит.");
        if (limit.Amount < ActualTotalAmount)
            throw new DomainException("operations.overrun_limit_too_low", "Новый лимит не покрывает фактический расход.");

        _overrunDecisions.Add(new ExecutionOverrunDecision(decisionId, OrganizationId, Id, actorUserId,
            ActualTotalAmount, limit.Amount, limit.Currency, normalizedReason, now));
        Touch(now);
        return true;
    }

    public void Complete(Guid actorUserId, long expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (Status == ReconditioningExecutionStatus.Completed) return;
        if (Status is not (ReconditioningExecutionStatus.InProgress or ReconditioningExecutionStatus.Blocked))
            throw new DomainException("operations.execution_not_active", "Завершить можно только активный execution.");
        if (_workOrders.Any(x => x.IsMandatory && x.Status != WorkOrderStatus.Completed))
            throw new DomainException("operations.mandatory_work_incomplete", "Все обязательные работы должны быть завершены.");
        if (_workOrders.Any(x => x.Status is WorkOrderStatus.InProgress or WorkOrderStatus.Blocked
                or WorkOrderStatus.ReturnedForRework))
            throw new DomainException("operations.work_incomplete", "Все активные work orders должны быть завершены.");
        if (ActualTotalAmount > ApprovedLimitAmount
            && !_overrunDecisions.Any(x => x.ApprovedLimitAmount >= ActualTotalAmount && x.Currency == Currency))
            throw new DomainException("operations.overrun_approval_required",
                "Для завершения требуется отдельное согласование перерасхода.");

        Status = ReconditioningExecutionStatus.Completed;
        CompletedAt = now;
        Touch(now);
    }

    public int GenerateDeadlineNotifications(DateTimeOffset now, TimeSpan dueSoonWindow)
    {
        var created = 0;
        foreach (var work in _workOrders.Where(x => x.Status is WorkOrderStatus.Scheduled or WorkOrderStatus.InProgress
                     or WorkOrderStatus.Blocked or WorkOrderStatus.ReturnedForRework))
        {
            var type = work.DueAt < now ? ExecutionNotificationType.Overdue
                : work.DueAt <= now.Add(dueSoonWindow) ? ExecutionNotificationType.DueSoon
                : (ExecutionNotificationType?)null;
            if (type is null) continue;
            var deduplicationKey = $"{work.Id:N}:{work.DueAt.UtcTicks}:{(int)type}";
            if (_notifications.Any(x => x.DeduplicationKey == deduplicationKey)) continue;
            _notifications.Add(new ExecutionNotification(Guid.NewGuid(), OrganizationId, Id, work.Id,
                type.Value, deduplicationKey, now));
            created++;
        }
        if (created > 0) Touch(now);
        return created;
    }

    private ExecutionWorkOrder FindWork(Guid workOrderId) => _workOrders.SingleOrDefault(x => x.Id == workOrderId)
        ?? throw new NotFoundException("Work order не найден.");

    private void EnsureMutable(long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status is ReconditioningExecutionStatus.Completed or ReconditioningExecutionStatus.Cancelled)
            throw new DomainException("operations.execution_immutable", "Закрытый execution неизменяем.");
    }

    private void EnsureInProgress(long expectedVersion, bool allowBlockedExecution = false)
    {
        EnsureVersion(expectedVersion);
        if (Status != ReconditioningExecutionStatus.InProgress
            && !(allowBlockedExecution && Status == ReconditioningExecutionStatus.Blocked))
            throw new DomainException("operations.execution_not_in_progress", "Execution не находится в работе.");
    }

    private void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion)
            throw new ConflictException("operations.version_conflict", "Execution был изменён. Обновите данные.");
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    private static string RequireText(string? value, int maxLength, string message)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new DomainException("operations.reason_required", message);
        if (normalized.Length > maxLength) throw new DomainException("operations.text_too_long", message);
        return normalized;
    }
}

public sealed class ExecutionWorkOrder
{
    private readonly List<WorkOrderMaterialMovement> _materialMovements = [];
    private ExecutionWorkOrder() { }

    private ExecutionWorkOrder(Guid id, Guid organizationId, Guid executionId, ExecutionSourceWork source,
        DateTimeOffset now)
    {
        Id = id;
        OrganizationId = organizationId;
        ExecutionId = executionId;
        SourcePlanWorkId = source.PlanWorkId;
        SourceDefectId = source.SourceDefectId;
        Title = RequireText(source.Title, 300);
        IsMandatory = source.IsMandatory;
        ExecutorType = RequireText(source.ExecutorType, 30);
        AssigneeName = RequireText(source.ExecutorName, 300);
        PlannedLaborAmount = new Money(source.PlannedLaborAmount, source.Currency).Amount;
        PlannedPartsAmount = new Money(source.PlannedPartsAmount, source.Currency).Amount;
        Currency = new Money(0, source.Currency).Currency;
        DueAt = now.AddDays(source.EstimatedDurationDays);
        Status = WorkOrderStatus.Scheduled;
        SettlementStatus = source.ExecutorType.Equals("External", StringComparison.OrdinalIgnoreCase)
            ? ContractorSettlementStatus.AwaitingConfirmation
            : ContractorSettlementStatus.NotRequired;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ExecutionId { get; private set; }
    public Guid SourcePlanWorkId { get; private set; }
    public Guid SourceDefectId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public bool IsMandatory { get; private set; }
    public string ExecutorType { get; private set; } = string.Empty;
    public string AssigneeName { get; private set; } = string.Empty;
    public DateTimeOffset DueAt { get; private set; }
    public WorkOrderStatus Status { get; private set; }
    public decimal PlannedLaborAmount { get; private set; }
    public decimal PlannedPartsAmount { get; private set; }
    public decimal ActualLaborHours { get; private set; }
    public decimal ActualLaborAmount { get; private set; }
    public decimal ActualExternalAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string? ContractorName { get; private set; }
    public string? InvoiceReference { get; private set; }
    public DateTimeOffset? ContractorDueAt { get; private set; }
    public ContractorSettlementStatus SettlementStatus { get; private set; }
    public Guid? SettlementChangedByUserId { get; private set; }
    public DateTimeOffset? SettlementChangedAt { get; private set; }
    public string? SettlementComment { get; private set; }
    public string? BlockReason { get; private set; }
    public string? CompletionComment { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<WorkOrderMaterialMovement> MaterialMovements => _materialMovements;
    public decimal MaterialAmount => _materialMovements.Sum(x => x.SignedAmount);

    internal static ExecutionWorkOrder Create(Guid id, Guid organizationId, Guid executionId,
        ExecutionSourceWork source, DateTimeOffset now) => new(id, organizationId, executionId, source, now);

    internal void Schedule(string? assigneeName, DateTimeOffset dueAt, DateTimeOffset now)
    {
        if (Status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled)
            throw new DomainException("operations.work_immutable", "Закрытый work order нельзя переназначить.");
        if (dueAt <= now) throw new DomainException("operations.due_date_invalid", "Срок должен быть в будущем.");
        AssigneeName = RequireText(assigneeName, 300);
        DueAt = dueAt;
        UpdatedAt = now;
    }

    internal void Start(DateTimeOffset now)
    {
        if (Status == WorkOrderStatus.InProgress) return;
        if (Status is not (WorkOrderStatus.Scheduled or WorkOrderStatus.ReturnedForRework))
            throw new DomainException("operations.work_cannot_start", "Work order нельзя начать из текущего статуса.");
        Status = WorkOrderStatus.InProgress;
        StartedAt ??= now;
        BlockReason = null;
        UpdatedAt = now;
    }

    internal void RecordActuals(decimal laborHours, decimal laborAmount, decimal externalAmount, string currency,
        string? contractorName, string? invoiceReference, DateTimeOffset? contractorDueAt, DateTimeOffset now)
    {
        EnsureWorkActive();
        if (laborHours < 0 || laborHours > 10000)
            throw new DomainException("operations.labor_hours_invalid", "Трудозатраты должны быть неотрицательными.");
        var labor = new Money(laborAmount, currency);
        var external = new Money(externalAmount, currency);
        if (labor.Currency != Currency || external.Currency != Currency)
            throw new DomainException("operations.actual_currency_mismatch", "Фактические расходы должны быть в валюте execution.");
        ActualLaborHours = decimal.Round(laborHours, 2, MidpointRounding.ToEven);
        ActualLaborAmount = labor.Amount;
        ActualExternalAmount = external.Amount;
        ContractorName = NormalizeOptional(contractorName, 300);
        InvoiceReference = NormalizeOptional(invoiceReference, 200);
        ContractorDueAt = contractorDueAt;
        if (external.Amount > 0)
        {
            if (ContractorName is null)
                throw new DomainException("operations.contractor_required", "Для внешнего расхода укажите подрядчика.");
            if (SettlementStatus == ContractorSettlementStatus.NotRequired)
                SettlementStatus = ContractorSettlementStatus.AwaitingConfirmation;
        }
        UpdatedAt = now;
    }

    internal bool AddMaterial(Guid movementId, MaterialMovementType type, string? name, decimal quantity,
        string? unit, decimal unitCost, string? currency, string? supplierName, Guid actorUserId,
        DateTimeOffset now)
    {
        EnsureWorkActive();
        if (!Enum.IsDefined(type)) throw new DomainException("operations.material_type_invalid", "Недопустимый тип движения материала.");
        if (movementId == Guid.Empty) throw new DomainException("operations.material_id_required", "Movement ID обязателен.");
        var normalizedName = RequireText(name, 300);
        var normalizedUnit = RequireText(unit, 30);
        if (quantity <= 0 || quantity > 100000)
            throw new DomainException("operations.material_quantity_invalid", "Количество должно быть положительным.");
        var cost = new Money(unitCost, currency);
        if (cost.Currency != Currency)
            throw new DomainException("operations.material_currency_mismatch", "Материал должен быть в валюте execution.");
        var normalizedQuantity = decimal.Round(quantity, 3, MidpointRounding.ToEven);
        var existing = _materialMovements.SingleOrDefault(x => x.Id == movementId);
        if (existing is not null)
        {
            if (!existing.Matches(type, normalizedName, normalizedQuantity, normalizedUnit, cost.Amount,
                    cost.Currency, NormalizeOptional(supplierName, 300)))
                throw new ConflictException("operations.material_id_conflict", "Movement ID уже использован для другого движения.");
            return false;
        }
        if (type == MaterialMovementType.Returned)
        {
            var consumed = _materialMovements.Where(x => x.Name == normalizedName && x.Unit == normalizedUnit)
                .Sum(x => x.Type == MaterialMovementType.Consumed ? x.Quantity : -x.Quantity);
            if (normalizedQuantity > consumed)
                throw new DomainException("operations.material_return_exceeds_consumed",
                    "Возврат материала не может превышать списанное количество.");
        }
        _materialMovements.Add(new WorkOrderMaterialMovement(movementId, OrganizationId, ExecutionId, Id, type,
            normalizedName, normalizedQuantity, normalizedUnit, cost.Amount, cost.Currency,
            NormalizeOptional(supplierName, 300), actorUserId, now));
        UpdatedAt = now;
        return true;
    }

    internal void Block(string? reason, DateTimeOffset now)
    {
        if (Status != WorkOrderStatus.InProgress)
            throw new DomainException("operations.work_not_in_progress", "Заблокировать можно только выполняемую работу.");
        BlockReason = RequireText(reason, 2000);
        Status = WorkOrderStatus.Blocked;
        UpdatedAt = now;
    }

    internal void Resume(DateTimeOffset now)
    {
        if (Status != WorkOrderStatus.Blocked)
            throw new DomainException("operations.work_not_blocked", "Возобновить можно только заблокированную работу.");
        Status = WorkOrderStatus.InProgress;
        BlockReason = null;
        UpdatedAt = now;
    }

    internal void Complete(string? comment, DateTimeOffset now)
    {
        if (Status is not (WorkOrderStatus.InProgress or WorkOrderStatus.ReturnedForRework))
            throw new DomainException("operations.work_cannot_complete", "Работа не находится в статусе выполнения.");
        CompletionComment = NormalizeOptional(comment, 2000);
        Status = WorkOrderStatus.Completed;
        CompletedAt = now;
        BlockReason = null;
        UpdatedAt = now;
    }

    internal void ReturnForRework(string? reason, DateTimeOffset now)
    {
        if (Status != WorkOrderStatus.Completed)
            throw new DomainException("operations.work_not_completed", "На доработку можно вернуть только завершённую работу.");
        BlockReason = RequireText(reason, 2000);
        Status = WorkOrderStatus.ReturnedForRework;
        CompletedAt = null;
        UpdatedAt = now;
    }

    internal void SetSettlement(ContractorSettlementStatus status, string? comment, Guid actorUserId,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(status))
            throw new DomainException("operations.settlement_status_invalid", "Недопустимый статус расчёта.");
        if (ActualExternalAmount <= 0 && status != ContractorSettlementStatus.NotRequired)
            throw new DomainException("operations.settlement_not_required", "У work order нет внешнего расхода.");
        SettlementStatus = status;
        SettlementComment = NormalizeOptional(comment, 2000);
        SettlementChangedByUserId = actorUserId;
        SettlementChangedAt = now;
        UpdatedAt = now;
    }

    private void EnsureWorkActive()
    {
        if (Status != WorkOrderStatus.InProgress)
            throw new DomainException("operations.work_not_in_progress", "Фактические расходы можно менять только во время выполнения.");
    }

    private static string RequireText(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
            throw new DomainException("operations.text_invalid", "Обязательный текст не заполнен или слишком длинный.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
            throw new DomainException("operations.text_too_long", "Текст превышает допустимую длину.");
        return normalized;
    }
}

public sealed class WorkOrderMaterialMovement
{
    private WorkOrderMaterialMovement() { }
    internal WorkOrderMaterialMovement(Guid id, Guid organizationId, Guid executionId, Guid workOrderId,
        MaterialMovementType type, string name, decimal quantity, string unit, decimal unitCost, string currency,
        string? supplierName, Guid actorUserId, DateTimeOffset occurredAt) =>
        (Id, OrganizationId, ExecutionId, WorkOrderId, Type, Name, Quantity, Unit, UnitCost, Currency,
            SupplierName, ActorUserId, OccurredAt) =
        (id, organizationId, executionId, workOrderId, type, name, quantity, unit, unitCost, currency,
            supplierName, actorUserId, occurredAt);

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ExecutionId { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public MaterialMovementType Type { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal UnitCost { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string? SupplierName { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public decimal SignedAmount => (Type == MaterialMovementType.Consumed ? 1 : -1) * Quantity * UnitCost;

    internal bool Matches(MaterialMovementType type, string name, decimal quantity, string unit,
        decimal unitCost, string currency, string? supplierName) => Type == type && Name == name
        && Quantity == quantity && Unit == unit && UnitCost == unitCost && Currency == currency
        && SupplierName == supplierName;
}

public sealed class ExecutionOverrunDecision
{
    private ExecutionOverrunDecision() { }
    internal ExecutionOverrunDecision(Guid id, Guid organizationId, Guid executionId, Guid actorUserId,
        decimal actualAmount, decimal approvedLimitAmount, string currency, string reason, DateTimeOffset decidedAt) =>
        (Id, OrganizationId, ExecutionId, ActorUserId, ActualAmount, ApprovedLimitAmount, Currency, Reason, DecidedAt) =
        (id, organizationId, executionId, actorUserId, actualAmount, approvedLimitAmount, currency, reason, decidedAt);

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ExecutionId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public decimal ActualAmount { get; private set; }
    public decimal ApprovedLimitAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset DecidedAt { get; private set; }
}

public sealed class ExecutionNotification
{
    private ExecutionNotification() { }
    internal ExecutionNotification(Guid id, Guid organizationId, Guid executionId, Guid workOrderId,
        ExecutionNotificationType type, string deduplicationKey, DateTimeOffset createdAt) =>
        (Id, OrganizationId, ExecutionId, WorkOrderId, Type, DeduplicationKey, CreatedAt) =
        (id, organizationId, executionId, workOrderId, type, deduplicationKey, createdAt);

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ExecutionId { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public ExecutionNotificationType Type { get; private set; }
    public string DeduplicationKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
