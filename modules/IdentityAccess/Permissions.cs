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
    public const string CrmCustomersView = "crm.customers.view";
    public const string CrmCustomersEdit = "crm.customers.edit";
    public const string CrmCustomersMerge = "crm.customers.merge";
    public const string CrmLeadsView = "crm.leads.view";
    public const string CrmLeadsCreate = "crm.leads.create";
    public const string CrmLeadsAssign = "crm.leads.assign";
    public const string CrmLeadsWork = "crm.leads.work";
    public const string SalesVisitsView = "sales.visits.view";
    public const string SalesVisitsCreate = "sales.visits.create";
    public const string SalesVisitsEdit = "sales.visits.edit";
    public const string SalesVisitsComplete = "sales.visits.complete";
    public const string SalesOffersView = "sales.offers.view";
    public const string SalesOffersCreate = "sales.offers.create";
    public const string SalesOffersEdit = "sales.offers.edit";
    public const string SalesOffersSubmit = "sales.offers.submit";
    public const string SalesOffersAutoApprove = "sales.offers.auto_approve";
    public const string SalesOffersApprove = "sales.offers.approve";
    public const string SalesOffersCancel = "sales.offers.cancel";
    public const string ReservationsView = "reservations.view";
    public const string ReservationsCreate = "reservations.create";
    public const string ReservationsEdit = "reservations.edit";
    public const string ReservationsExtend = "reservations.extend";
    public const string ReservationsCancel = "reservations.cancel";
    public const string ReservationsDeposit = "reservations.deposit";
    public const string DealsView = "deals.view";
    public const string DealsCreate = "deals.create";
    public const string DealsEdit = "deals.edit";
    public const string DealsPayments = "deals.payments";
    public const string DealsDocuments = "deals.documents";
    public const string DealsHandover = "deals.handover";
    public const string DealsCancel = "deals.cancel";
    public const string FinanceView = "finance.view";
    public const string FinanceEditCosts = "finance.edit_costs";
    public const string FinanceExport = "finance.export";

    public static readonly string[] FinanceOperator =
    [
        FinanceView,
        FinanceEditCosts,
        FinanceExport
    ];

    public static readonly string[] DealOperator =
    [
        DealsView,
        DealsCreate,
        DealsEdit,
        DealsPayments,
        DealsDocuments,
        DealsHandover,
        DealsCancel
    ];

    public static readonly string[] ReservationOperator =
    [
        ReservationsView,
        ReservationsCreate,
        ReservationsEdit,
        ReservationsExtend,
        ReservationsCancel,
        ReservationsDeposit
    ];

    public static readonly string[] SalesOperator =
    [
        SalesVisitsView,
        SalesVisitsCreate,
        SalesVisitsEdit,
        SalesVisitsComplete,
        SalesOffersView,
        SalesOffersCreate,
        SalesOffersEdit,
        SalesOffersSubmit,
        SalesOffersAutoApprove,
        SalesOffersCancel,
        .. ReservationOperator,
        .. DealOperator,
        .. FinanceOperator
    ];

    public static readonly string[] SalesManager =
    [
        SalesVisitsView,
        SalesOffersView,
        SalesOffersApprove,
        ReservationsView,
        DealsView,
        FinanceView,
        FinanceExport
    ];

    public static readonly string[] CrmOperator =
    [
        CrmCustomersView,
        CrmCustomersEdit,
        CrmLeadsView,
        CrmLeadsCreate,
        CrmLeadsWork
    ];

    public static readonly string[] CrmManager =
    [
        .. CrmOperator,
        CrmCustomersMerge,
        CrmLeadsAssign,
        .. SalesOperator,
        .. SalesManager
    ];

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
        .. ListingOperator,
        .. CrmManager
    ];
}
