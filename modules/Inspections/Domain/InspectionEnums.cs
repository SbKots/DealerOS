namespace DealerOS.Modules.Inspections.Domain;

public enum InspectionStatus
{
    Draft = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4
}

public enum InspectionCategory
{
    Body = 1,
    Interior = 2,
    Engine = 3,
    Transmission = 4,
    Suspension = 5,
    Brakes = 6,
    Steering = 7,
    Electrical = 8,
    WheelsAndTires = 9,
    DocumentsAndEquipment = 10,
    TestDrive = 11
}

public enum InspectionItemResult
{
    Pending = 1,
    Pass = 2,
    Fail = 3,
    NotApplicable = 4
}

public enum DefectSeverity
{
    Minor = 1,
    Major = 2,
    Critical = 3
}
