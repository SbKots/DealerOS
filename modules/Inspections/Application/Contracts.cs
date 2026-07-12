using DealerOS.Modules.Inspections.Domain;
using DealerOS.Modules.Vehicles.Domain;

namespace DealerOS.Modules.Inspections.Application;

public sealed record StartInspectionRequest(int MileageKm, Guid? TemplateId = null);
public sealed record SaveInspectionItemRequest(InspectionItemResult Result, string? Comment, long ExpectedVersion);
public sealed record AddDefectRequest(Guid DefectId, InspectionCategory Category, string? Title, string? Description,
    DefectSeverity Severity, string? Recommendation, decimal? EstimatedRepairAmount, string? Currency,
    bool RepairRequired, bool BlocksPublication, bool BlocksTestDrive, bool BlocksSale, long ExpectedVersion);
public sealed record CompleteInspectionRequest(string? FinalComment, long ExpectedVersion);
public sealed record CancelInspectionRequest(long ExpectedVersion);
public sealed record CreateTemplateRequest(string? Name, IReadOnlyList<CreateTemplateItemRequest> Items);
public sealed record CreateTemplateItemRequest(string? Key, InspectionCategory Category, string? Label,
    string? Description, bool IsRequired, int SortOrder);

public sealed record InspectionQueueVehicleResponse(Guid Id, Guid BranchId, string BranchName, string Vin,
    string Make, string Model, int Year, int MileageKm, DateTimeOffset AcceptedAt);
public sealed record InspectionSummaryResponse(Guid Id, Guid VehicleId, Guid BranchId, Guid InspectorId,
    string Status, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, int MileageKm, string TemplateName,
    int TemplateVersion, int Revision, Guid? CorrectsInspectionId, bool NeedsReconditioning, int DefectCount,
    long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record InspectionItemResponse(Guid Id, string Key, string Category, string Label, string? Description,
    bool IsRequired, int SortOrder, string Result, string? Comment, DateTimeOffset UpdatedAt);
public sealed record InspectionPhotoResponse(Guid Id, string OriginalFileName, string ContentType, long SizeBytes,
    string DownloadUrl, Guid CreatedByUserId, DateTimeOffset CreatedAt);
public sealed record InspectionDefectResponse(Guid Id, string Category, string Title, string Description,
    string Severity, string? Recommendation, decimal? EstimatedRepairAmount, string? Currency,
    bool RepairRequired, bool BlocksPublication, bool BlocksTestDrive, bool BlocksSale,
    Guid CreatedByUserId, DateTimeOffset CreatedAt, IReadOnlyList<InspectionPhotoResponse> Photos);
public sealed record InspectionDetailResponse(Guid Id, Guid VehicleId, Guid BranchId, string BranchName,
    Guid InspectorId, string InspectorName, string Status, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    int MileageKm, string? FinalComment, string TemplateName, int TemplateVersion, int Revision,
    Guid? CorrectsInspectionId, bool NeedsReconditioning, long Version, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, IReadOnlyList<InspectionItemResponse> Items,
    IReadOnlyList<InspectionDefectResponse> Defects);
public sealed record InspectionTemplateResponse(Guid Id, string Name, int Version, DateTimeOffset CreatedAt,
    IReadOnlyList<InspectionTemplateItemResponse> Items);
public sealed record InspectionTemplateItemResponse(Guid Id, string Key, string Category, string Label,
    string? Description, bool IsRequired, int SortOrder);
public sealed record InspectionPhotoDownload(Stream Content, string ContentType, string FileName, long SizeBytes);
public sealed record NormalizedInspectionImage(Stream Content, string ContentType, string Extension, long SizeBytes);

public interface IInspectionStore
{
    Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<Inspection?> FindAsync(Guid organizationId, Guid inspectionId, CancellationToken cancellationToken);
    Task<Inspection?> FindActiveAsync(Guid organizationId, Guid vehicleId, CancellationToken cancellationToken);
    Task<InspectionTemplate?> FindTemplateAsync(Guid organizationId, Guid templateId, CancellationToken cancellationToken);
    Task<InspectionTemplate?> FindLatestTemplateAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<int> GetNextTemplateVersionAsync(Guid organizationId, CancellationToken cancellationToken);
    Task AddAsync(Inspection inspection, CancellationToken cancellationToken);
    Task AddTemplateAsync(InspectionTemplate template, CancellationToken cancellationToken);
    Task<IReadOnlyList<InspectionQueueVehicleResponse>> ListQueueAsync(Guid organizationId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<InspectionSummaryResponse>> ListForVehicleAsync(Guid organizationId, Guid vehicleId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task<InspectionDetailResponse?> GetResponseAsync(Guid organizationId, Guid inspectionId,
        IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task<InspectionTemplateResponse?> GetTemplateResponseAsync(Guid organizationId, Guid templateId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<InspectionTemplateResponse>> ListTemplatesAsync(Guid organizationId,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IInspectionPhotoStorage
{
    Task EnsureBucketAsync(CancellationToken cancellationToken);
    Task PutAsync(string objectKey, Stream content, long sizeBytes, string contentType, CancellationToken cancellationToken);
    Task<Stream> GetAsync(string objectKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public interface IInspectionImageProcessor
{
    Task<NormalizedInspectionImage> NormalizeAsync(Stream input, long declaredLength, string originalFileName,
        string? declaredContentType, CancellationToken cancellationToken);
}
