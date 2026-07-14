namespace DealerOS.Modules.Vehicles.Domain;

public enum VehicleStatus
{
    IntakeDraft = 1,
    InStock = 2,
    InspectionInProgress = 3,
    ReconditioningRequired = 4,
    InspectionPassed = 5,
    ReadyForSale = 6,
    Reserved = 7,
    SaleInProgress = 8,
    Sold = 9
}
