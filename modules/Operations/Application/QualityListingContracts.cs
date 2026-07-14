using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Vehicles.Domain;

namespace DealerOS.Modules.Operations.Application;

public enum VehicleMediaVariant { Thumbnail, Medium, Large, Original }

public sealed record AddQualityObservationRequest(Guid ObservationId, QualityObservationSeverity Severity,
    Guid? WorkOrderId, Guid? DefectId, string? Comment, bool RequiresRework, long ExpectedVersion);
public sealed record QualityDecisionRequest(string? Comment, long ExpectedVersion);
public sealed record QualityObservationResponse(Guid Id, string Severity, Guid? WorkOrderId, Guid? DefectId,
    string Comment, bool RequiresRework, DateTimeOffset CreatedAt);
public sealed record QualityCheckResponse(Guid Id, Guid VehicleId, Guid ExecutionId, int Revision, string Status,
    string ChecklistSnapshotJson, string? DecisionComment, long Version, DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt, IReadOnlyList<QualityObservationResponse> Observations);
public sealed record QualityQueueWorkOrderResponse(Guid Id, Guid SourceDefectId, string Title);
public sealed record QualityQueueResponse(Guid ExecutionId, Guid VehicleId, string Vin, string Make, string Model,
    DateTimeOffset CompletedAt, decimal ActualTotalAmount, string Currency, Guid? LatestQualityCheckId,
    string? LatestQualityStatus, int? LatestQualityRevision,
    IReadOnlyList<QualityQueueWorkOrderResponse> WorkOrders);

public sealed record VehicleMediaResponse(Guid Id, Guid VehicleId, string? Category, string OriginalFileName,
    string ContentType, long SizeBytes, int Width, int Height, int SortOrder, bool IsCover,
    bool IsIncludedInListing, string? Caption, decimal? FocalPointX, decimal? FocalPointY,
    Guid? SourceInspectionPhotoId, long Version, string ThumbnailUrl, string MediumUrl, string LargeUrl,
    string OriginalUrl, string DownloadUrl, DateTimeOffset CreatedAt);
public sealed record VehicleMediaDownload(Stream Content, string ContentType, string FileName);
public sealed record UpdateMediaOrderRequest(int SortOrder, long ExpectedVersion);
public sealed record SetMediaCoverRequest(long ExpectedVersion);
public sealed record UpdateMediaMetadataRequest(VehicleMediaCategory? Category, string? Caption,
    decimal? FocalPointX, decimal? FocalPointY, long ExpectedVersion);
public sealed record SetMediaListingSelectionRequest(bool IsIncluded, long ExpectedVersion);
public sealed record DeleteMediaRequest(long ExpectedVersion);
public sealed record ReorderMediaItemRequest(Guid MediaId, int SortOrder, long ExpectedVersion);
public sealed record ReorderVehicleMediaRequest(IReadOnlyList<ReorderMediaItemRequest> Items);
public sealed record ImportInspectionPhotoRequest(Guid MediaId, Guid InspectionPhotoId,
    VehicleMediaCategory? Category);
public sealed record InspectionMediaSourceResponse(Guid PhotoId, Guid InspectionId, Guid DefectId,
    string DefectTitle, string OriginalFileName, DateTimeOffset CreatedAt, string PreviewUrl);
public sealed record UpdateListingContentRequest(Guid CommandId, string? Equipment, string? Advantages,
    string? ConditionDescription, decimal PublicPriceAmount, string? Currency, string? TemplateName,
    int TemplateVersion, long ExpectedVersion);
public sealed record ListingCommandRequest(Guid CommandId, long ExpectedVersion);
public sealed record PublicationCommandRequest(Guid CommandId, string? Channel, string? ExternalId,
    string? ExternalUrl, string? Error);
public sealed record ChannelPublicationResponse(Guid Id, string Channel, string Status, string? ExternalId,
    string? ExternalUrl, string? Error, DateTimeOffset? ExportedAt, DateTimeOffset? PublishedAt,
    DateTimeOffset? UnpublishedAt);
public sealed record ListingHistoryResponse(Guid CommandId, string Action, decimal PublicPriceAmount,
    string Currency, DateTimeOffset OccurredAt);
public sealed record ListingContentResponse(Guid Id, Guid VehicleId, int Revision, string Status,
    string VehicleMake, string VehicleModel, int VehicleYear, int MileageKm, string Equipment, string Advantages,
    string ConditionDescription, decimal PublicPriceAmount, string Currency, string TemplateName,
    int TemplateVersion, string? SnapshotJson, long Version, DateTimeOffset? ReadyAt,
    IReadOnlyList<ChannelPublicationResponse> Publications, IReadOnlyList<ListingHistoryResponse> History);
public sealed record ListingExportResponse(string FileName, string ContentType, string Payload,
    ListingContentResponse Listing);

public sealed record MediaRegistration(Guid OrganizationId, Guid VehicleId, VehicleMedia Media);
public sealed record InspectionMediaSource(Guid PhotoId, Guid InspectionId, Guid DefectId, Guid VehicleId,
    Guid BranchId, string DefectTitle, string OriginalFileName, string ObjectKey, string ContentType,
    long SizeBytes, DateTimeOffset CreatedAt);

public interface IQualityListingStore
{
    Task<ReconditioningExecution?> FindExecutionAsync(Guid organizationId, Guid executionId,
        CancellationToken cancellationToken);
    Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<QualityCheck?> FindQualityCheckAsync(Guid organizationId, Guid qualityCheckId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ReconditioningExecution>> ListExecutionsForQualityAsync(Guid organizationId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task<QualityCheck?> FindLatestQualityCheckAsync(Guid organizationId, Guid executionId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<QualityCheck>> ListQualityChecksAsync(Guid organizationId, Guid vehicleId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task AddQualityCheckAsync(QualityCheck qualityCheck, CancellationToken cancellationToken);
    Task<MediaRegistration?> FindMediaRegistrationAsync(Guid mediaId, CancellationToken cancellationToken);
    Task<VehicleMedia?> FindMediaAsync(Guid organizationId, Guid mediaId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VehicleMedia>> ListMediaAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken);
    Task<int> CountMediaAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<InspectionMediaSource?> FindInspectionMediaSourceAsync(Guid organizationId, Guid vehicleId,
        Guid inspectionPhotoId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InspectionMediaSourceResponse>> ListInspectionMediaSourcesAsync(Guid organizationId,
        Guid vehicleId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task AddMediaAsync(VehicleMedia media, CancellationToken cancellationToken);
    void RemoveMedia(VehicleMedia media);
    Task QueueObjectDeletionAsync(Guid organizationId, string objectKey, DateTimeOffset now,
        CancellationToken cancellationToken);
    Task CompleteObjectDeletionAsync(Guid organizationId, string objectKey, CancellationToken cancellationToken);
    Task<bool> HasImmutableListingAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<ListingContent?> FindListingAsync(Guid organizationId, Guid listingId, CancellationToken cancellationToken);
    Task<ListingContent?> FindLatestListingAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken);
    Task AddListingAsync(ListingContent listing, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    void ResetTracking();
}
