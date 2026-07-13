namespace DealerOS.Modules.Reconditioning.Domain;

public enum ReconditioningPlanStatus
{
    Draft = 1,
    Submitted = 2,
    ChangesRequested = 3,
    Approved = 4,
    Rejected = 5,
    Cancelled = 6
}

public enum ReconditioningWorkCategory
{
    Body = 1,
    Mechanical = 2,
    Interior = 3,
    Electrical = 4,
    WheelsAndTires = 5,
    Detailing = 6,
    Documents = 7,
    Other = 8
}

public enum ReconditioningWorkPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4
}

public enum ReconditioningExecutorType
{
    Internal = 1,
    External = 2
}

public enum ReconditioningDecisionType
{
    Approved = 1,
    Rejected = 2,
    ChangesRequested = 3
}
