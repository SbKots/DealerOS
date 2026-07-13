namespace DealerOS.Modules.IdentityAccess;

public static class Permissions
{
    public const string VehiclesRead = "vehicles.read";
    public const string VehiclesCreate = "vehicles.create";
    public const string VehiclesAccept = "vehicles.accept";
    public const string VehicleInspectionsView = "vehicles.inspections.view";
    public const string VehicleInspectionsCreate = "vehicles.inspections.create";
    public const string VehicleInspectionsEdit = "vehicles.inspections.edit";
    public const string VehicleInspectionsComplete = "vehicles.inspections.complete";
    public const string VehicleInspectionsCancel = "vehicles.inspections.cancel";
    public const string VehicleInspectionsManageTemplates = "vehicles.inspections.manage_templates";

    public static readonly string[] InspectionOperator =
    [
        VehiclesRead,
        VehicleInspectionsView,
        VehicleInspectionsCreate,
        VehicleInspectionsEdit,
        VehicleInspectionsComplete,
        VehicleInspectionsCancel
    ];

    public static readonly string[] VehicleOperator =
    [
        VehiclesRead,
        VehiclesCreate,
        VehiclesAccept,
        .. InspectionOperator,
        VehicleInspectionsManageTemplates
    ];
}
