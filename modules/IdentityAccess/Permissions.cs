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
    public const string ReconditioningView = "reconditioning.view";
    public const string ReconditioningCreate = "reconditioning.create";
    public const string ReconditioningEdit = "reconditioning.edit";
    public const string ReconditioningSubmit = "reconditioning.submit";
    public const string ReconditioningApprove = "reconditioning.approve";
    public const string ReconditioningCancel = "reconditioning.cancel";
    public const string OperationsView = "operations.view";
    public const string OperationsCreate = "operations.create";
    public const string OperationsEdit = "operations.edit";
    public const string OperationsComplete = "operations.complete";
    public const string OperationsApproveOverrun = "operations.approve_overrun";
    public const string OperationsManageSettlement = "operations.manage_settlement";
    public const string QualityView = "quality.view";
    public const string QualityCreate = "quality.create";
    public const string QualityDecide = "quality.decide";
    public const string ListingsView = "listings.view";
    public const string ListingsEdit = "listings.edit";
    public const string ListingsPublish = "listings.publish";

    public static readonly string[] QualityManager =
    [
        QualityView,
        QualityCreate,
        QualityDecide
    ];

    public static readonly string[] ListingOperator =
    [
        ListingsView,
        ListingsEdit,
        ListingsPublish
    ];

    public static readonly string[] OperationsOperator =
    [
        OperationsView,
        OperationsCreate,
        OperationsEdit,
        OperationsComplete
    ];

    public static readonly string[] OperationsManager =
    [
        OperationsView,
        OperationsApproveOverrun,
        OperationsManageSettlement
    ];

    public static readonly string[] ReconditioningOperator =
    [
        ReconditioningView,
        ReconditioningCreate,
        ReconditioningEdit,
        ReconditioningSubmit,
        ReconditioningCancel
    ];

    public static readonly string[] ReconditioningManager =
    [
        ReconditioningView,
        ReconditioningApprove
    ];

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
        VehicleInspectionsManageTemplates,
        .. ReconditioningOperator,
        ReconditioningApprove,
        .. OperationsOperator,
        .. OperationsManager,
        .. QualityManager,
        .. ListingOperator
    ];
}
