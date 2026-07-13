namespace DealerOS.Modules.Operations.Domain;

public enum ReconditioningExecutionStatus
{
    Draft = 1,
    InProgress = 2,
    Blocked = 3,
    Completed = 4,
    Cancelled = 5
}

public enum WorkOrderStatus
{
    Scheduled = 1,
    InProgress = 2,
    Blocked = 3,
    ReturnedForRework = 4,
    Completed = 5,
    Cancelled = 6
}

public enum MaterialMovementType
{
    Consumed = 1,
    Returned = 2
}

public enum ContractorSettlementStatus
{
    NotRequired = 1,
    AwaitingConfirmation = 2,
    AwaitingPayment = 3,
    Paid = 4,
    Disputed = 5,
    Cancelled = 6
}

public enum ExecutionNotificationType
{
    DueSoon = 1,
    Overdue = 2
}
