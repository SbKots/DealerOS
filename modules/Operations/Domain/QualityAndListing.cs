using System.Text.Json;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Operations.Domain;

public enum QualityCheckStatus { Draft = 1, Passed = 2, ReworkRequired = 3, Rejected = 4 }
public enum QualityObservationSeverity { Minor = 1, Major = 2, Critical = 3 }
public enum VehicleMediaCategory
{
    MainView = 1,
    Exterior = MainView,
    Interior = 2,
    Defect = 3,
    DamageHistory = Defect,
    Documents = 4,
    DocumentsInternal = Documents,
    Front = 5,
    Rear = 6,
    LeftSide = 7,
    RightSide = 8,
    Dashboard = 9,
    Trunk = 10,
    Engine = 11,
    Other = 12
}
public enum ListingContentStatus { Draft = 1, Ready = 2 }
public enum ChannelPublicationStatus { Draft = 1, Exported = 2, Published = 3, Failed = 4, Unpublished = 5 }

public sealed class QualityCheck
{
    private readonly List<QualityObservation> _observations = [];
    private QualityCheck() { }

    private QualityCheck(Guid organizationId, Guid branchId, Guid vehicleId, Guid executionId, int revision,
        Guid actorUserId, string checklistSnapshotJson, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        BranchId = branchId;
        VehicleId = vehicleId;
        ExecutionId = executionId;
        Revision = revision;
        CreatedByUserId = actorUserId;
        ChecklistSnapshotJson = checklistSnapshotJson;
        Status = QualityCheckStatus.Draft;
        CreatedAt = now;
        UpdatedAt = now;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid ExecutionId { get; private set; }
    public int Revision { get; private set; }
    public QualityCheckStatus Status { get; private set; }
    public string ChecklistSnapshotJson { get; private set; } = "{}";
    public string? DecisionComment { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<QualityObservation> Observations => _observations;

    public static QualityCheck Create(Guid organizationId, Guid branchId, Guid vehicleId, Guid executionId,
        int revision, Guid actorUserId, string checklistSnapshotJson, DateTimeOffset now)
    {
        if (revision <= 0) throw new DomainException("quality.invalid_revision", "Ревизия QC должна быть положительной.");
        if (string.IsNullOrWhiteSpace(checklistSnapshotJson))
            throw new DomainException("quality.checklist_required", "Checklist snapshot обязателен.");
        return new QualityCheck(organizationId, branchId, vehicleId, executionId, revision, actorUserId,
            checklistSnapshotJson, now);
    }

    public void AddObservation(Guid observationId, QualityObservationSeverity severity, Guid? workOrderId,
        Guid? defectId, string? comment, bool requiresRework, long expectedVersion, DateTimeOffset now)
    {
        EnsureDraft(expectedVersion);
        if (_observations.Any(x => x.Id == observationId)) return;
        if (requiresRework && workOrderId is null)
            throw new DomainException("quality.rework_work_required", "Замечание на доработку должно ссылаться на work order.");
        _observations.Add(new QualityObservation(observationId, OrganizationId, Id, severity, workOrderId,
            defectId, RequireText(comment, 2000, "Комментарий замечания обязателен."), requiresRework, now));
        Touch(now);
    }

    public void Pass(Guid actorUserId, string? comment, long expectedVersion, DateTimeOffset now)
    {
        EnsureDraft(expectedVersion);
        if (_observations.Any(x => x.RequiresRework || x.Severity is QualityObservationSeverity.Major
                or QualityObservationSeverity.Critical))
            throw new DomainException("quality.blocking_observations", "QC нельзя пройти при блокирующих замечаниях.");
        Decide(QualityCheckStatus.Passed, actorUserId, comment, now);
    }

    public IReadOnlyCollection<Guid> RequireRework(Guid actorUserId, string? comment, long expectedVersion,
        DateTimeOffset now)
    {
        EnsureDraft(expectedVersion);
        var workOrders = _observations.Where(x => x.RequiresRework).Select(x => x.WorkOrderId!.Value)
            .Distinct().ToArray();
        if (workOrders.Length == 0)
            throw new DomainException("quality.rework_observation_required", "Укажите конкретную работу для возврата.");
        Decide(QualityCheckStatus.ReworkRequired, actorUserId, comment, now);
        return workOrders;
    }

    public void Reject(Guid actorUserId, string? reason, long expectedVersion, DateTimeOffset now)
    {
        EnsureDraft(expectedVersion);
        Decide(QualityCheckStatus.Rejected, actorUserId,
            RequireText(reason, 2000, "Причина отклонения обязательна."), now);
    }

    private void Decide(QualityCheckStatus status, Guid actorUserId, string? comment, DateTimeOffset now)
    {
        Status = status;
        DecidedByUserId = actorUserId;
        DecisionComment = NormalizeOptional(comment, 2000);
        DecidedAt = now;
        Touch(now);
    }

    private void EnsureDraft(long expectedVersion)
    {
        if (Version != expectedVersion) throw new ConflictException("quality.version_conflict", "QC изменён конкурентно.");
        if (Status != QualityCheckStatus.Draft)
            throw new DomainException("quality.immutable", "Завершённая попытка QC неизменяема.");
    }

    private void Touch(DateTimeOffset now) { UpdatedAt = now; Version++; }
    private static string RequireText(string? value, int max, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("quality.required", message);
        var normalized = value.Trim();
        if (normalized.Length > max) throw new DomainException("quality.too_long", $"Максимальная длина — {max} символов.");
        return normalized;
    }
    private static string? NormalizeOptional(string? value, int max) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim().Length <= max ? value.Trim() : throw new DomainException("quality.too_long",
            $"Максимальная длина — {max} символов.");
}

public sealed class QualityObservation
{
    private QualityObservation() { }
    internal QualityObservation(Guid id, Guid organizationId, Guid qualityCheckId,
        QualityObservationSeverity severity, Guid? workOrderId, Guid? defectId, string comment,
        bool requiresRework, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        QualityCheckId = qualityCheckId;
        Severity = severity;
        WorkOrderId = workOrderId;
        DefectId = defectId;
        Comment = comment;
        RequiresRework = requiresRework;
        CreatedAt = createdAt;
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid QualityCheckId { get; private set; }
    public QualityObservationSeverity Severity { get; private set; }
    public Guid? WorkOrderId { get; private set; }
    public Guid? DefectId { get; private set; }
    public string Comment { get; private set; } = string.Empty;
    public bool RequiresRework { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class VehicleMedia
{
    private VehicleMedia() { }
    public VehicleMedia(Guid id, Guid organizationId, Guid branchId, Guid vehicleId, VehicleMediaCategory category,
        string objectKey, string originalFileName, string contentType, long sizeBytes, int sortOrder,
        Guid actorUserId, DateTimeOffset now) : this(id, organizationId, branchId, vehicleId, category, objectKey,
        objectKey, objectKey, objectKey, originalFileName, contentType, sizeBytes, 1, 1, 1, 1, 1, 1, 1, 1,
        null, true, sortOrder, actorUserId, now)
    { }

    public VehicleMedia(Guid id, Guid organizationId, Guid branchId, Guid vehicleId,
        VehicleMediaCategory? category, string objectKey, string thumbnailObjectKey, string mediumObjectKey,
        string largeObjectKey, string originalFileName, string contentType, long sizeBytes, int width, int height,
        int thumbnailWidth, int thumbnailHeight, int mediumWidth, int mediumHeight, int largeWidth, int largeHeight,
        Guid? sourceInspectionPhotoId, bool ownsOriginalObject, int sortOrder, Guid actorUserId, DateTimeOffset now)
    {
        if (sizeBytes <= 0) throw new DomainException("media.empty", "Пустой медиафайл недопустим.");
        if (width <= 0 || height <= 0 || thumbnailWidth <= 0 || thumbnailHeight <= 0 || mediumWidth <= 0
            || mediumHeight <= 0 || largeWidth <= 0 || largeHeight <= 0)
            throw new DomainException("media.invalid_dimensions", "Размеры изображения должны быть положительными.");
        Id = id; OrganizationId = organizationId; BranchId = branchId; VehicleId = vehicleId; Category = category;
        ObjectKey = objectKey; ThumbnailObjectKey = thumbnailObjectKey; MediumObjectKey = mediumObjectKey;
        LargeObjectKey = largeObjectKey; OriginalFileName = originalFileName; ContentType = contentType;
        SizeBytes = sizeBytes; Width = width; Height = height; ThumbnailWidth = thumbnailWidth;
        ThumbnailHeight = thumbnailHeight; MediumWidth = mediumWidth; MediumHeight = mediumHeight;
        LargeWidth = largeWidth; LargeHeight = largeHeight; SourceInspectionPhotoId = sourceInspectionPhotoId;
        OwnsOriginalObject = ownsOriginalObject; SortOrder = sortOrder; CreatedByUserId = actorUserId;
        CreatedAt = now; IsIncludedInListing = category != VehicleMediaCategory.Documents; Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid VehicleId { get; private set; }
    public VehicleMediaCategory? Category { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string ThumbnailObjectKey { get; private set; } = string.Empty;
    public string MediumObjectKey { get; private set; } = string.Empty;
    public string LargeObjectKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int ThumbnailWidth { get; private set; }
    public int ThumbnailHeight { get; private set; }
    public int MediumWidth { get; private set; }
    public int MediumHeight { get; private set; }
    public int LargeWidth { get; private set; }
    public int LargeHeight { get; private set; }
    public string? Caption { get; private set; }
    public decimal? FocalPointX { get; private set; }
    public decimal? FocalPointY { get; private set; }
    public Guid? SourceInspectionPhotoId { get; private set; }
    public bool OwnsOriginalObject { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsCover { get; private set; }
    public bool IsIncludedInListing { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; }
    public void SetSortOrder(int sortOrder, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (sortOrder < 0) throw new DomainException("media.invalid_sort", "Порядок кадра не может быть отрицательным.");
        SortOrder = sortOrder; Version++;
    }
    public void SetCover(bool value, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (value && Category == VehicleMediaCategory.Documents)
            throw new DomainException("media.internal_cover", "Внутренний документ нельзя сделать обложкой.");
        IsCover = value; Version++;
    }

    public void UpdateMetadata(VehicleMediaCategory? category, string? caption, decimal? focalPointX,
        decimal? focalPointY, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        var safeCaption = caption?.Trim();
        if (safeCaption?.Length > 500)
            throw new DomainException("media.caption_too_long", "Подпись не должна превышать 500 символов.");
        if (focalPointX is < 0 or > 1 || focalPointY is < 0 or > 1
            || (focalPointX is null) != (focalPointY is null))
            throw new DomainException("media.invalid_focal_point",
                "Точка кадрирования должна находиться внутри изображения.");
        if (IsCover && category == VehicleMediaCategory.Documents)
            throw new DomainException("media.internal_cover", "Документ нельзя сделать обложкой.");
        Category = category; Caption = string.IsNullOrWhiteSpace(safeCaption) ? null : safeCaption;
        FocalPointX = focalPointX; FocalPointY = focalPointY; Version++;
    }

    public void SetIncludedInListing(bool value, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (value && Category == VehicleMediaCategory.Documents)
            throw new DomainException("media.internal_listing", "Внутренний документ нельзя добавить в объявление.");
        IsIncludedInListing = value; Version++;
    }

    public IReadOnlyList<string> OwnedObjectKeys() => OwnsOriginalObject
        ? [ObjectKey, ThumbnailObjectKey, MediumObjectKey, LargeObjectKey]
        : [ThumbnailObjectKey, MediumObjectKey, LargeObjectKey];

    public void ValidateVersion(long expectedVersion) => EnsureVersion(expectedVersion);
    private void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion) throw new ConflictException("media.version_conflict", "Медиа изменено конкурентно.");
    }
}

public sealed class ListingContent
{
    private readonly List<ChannelPublication> _publications = [];
    private readonly List<ListingContentHistory> _history = [];
    private ListingContent() { }
    private ListingContent(Guid organizationId, Guid branchId, Guid vehicleId, int revision, string make,
        string model, int year, int mileageKm, Guid actorUserId, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); OrganizationId = organizationId; BranchId = branchId; VehicleId = vehicleId;
        Revision = revision; VehicleMake = make; VehicleModel = model; VehicleYear = year; MileageKm = mileageKm;
        CreatedByUserId = actorUserId; Status = ListingContentStatus.Draft; TemplateName = "DealerOS Default";
        TemplateVersion = 1; Currency = "RUB"; CreatedAt = now; UpdatedAt = now; Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid VehicleId { get; private set; }
    public int Revision { get; private set; }
    public ListingContentStatus Status { get; private set; }
    public string VehicleMake { get; private set; } = string.Empty;
    public string VehicleModel { get; private set; } = string.Empty;
    public int VehicleYear { get; private set; }
    public int MileageKm { get; private set; }
    public string Equipment { get; private set; } = string.Empty;
    public string Advantages { get; private set; } = string.Empty;
    public string ConditionDescription { get; private set; } = string.Empty;
    public decimal PublicPriceAmount { get; private set; }
    public string Currency { get; private set; } = "RUB";
    public string TemplateName { get; private set; } = string.Empty;
    public int TemplateVersion { get; private set; }
    public string? SnapshotJson { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<ChannelPublication> Publications => _publications;
    public IReadOnlyCollection<ListingContentHistory> History => _history;

    public static ListingContent Create(Guid organizationId, Guid branchId, Guid vehicleId, int revision,
        string make, string model, int year, int mileageKm, Guid actorUserId, DateTimeOffset now) =>
        new(organizationId, branchId, vehicleId, revision, make, model, year, mileageKm, actorUserId, now);

    public bool Update(Guid commandId, string? equipment, string? advantages, string? conditionDescription,
        decimal publicPriceAmount, string? currency, string? templateName, int templateVersion, Guid actorUserId,
        long expectedVersion, DateTimeOffset now)
    {
        var money = new Money(publicPriceAmount, currency);
        if (money.Amount <= 0) throw new DomainException("listing.price_required", "Публичная цена должна быть больше нуля.");
        var signature = JsonSerializer.Serialize(new
        {
            equipment,
            advantages,
            conditionDescription,
            money.Amount,
            money.Currency,
            templateName,
            templateVersion
        });
        if (EnsureCommand(commandId, "Updated", signature)) return false;
        EnsureDraft(expectedVersion);
        Equipment = Normalize(equipment, 4000); Advantages = Normalize(advantages, 4000);
        ConditionDescription = Require(conditionDescription, 4000, "Описание состояния обязательно.");
        PublicPriceAmount = money.Amount; Currency = money.Currency;
        TemplateName = Require(templateName, 200, "Шаблон обязателен.");
        if (templateVersion <= 0) throw new DomainException("listing.template_version", "Версия шаблона должна быть положительной.");
        TemplateVersion = templateVersion;
        AddHistory(commandId, "Updated", signature, actorUserId, now); Touch(now); return true;
    }

    public bool MarkReady(Guid commandId, string snapshotJson, Guid actorUserId, long expectedVersion,
        DateTimeOffset now)
    {
        if (EnsureCommand(commandId, "Ready", snapshotJson)) return false;
        EnsureDraft(expectedVersion);
        if (PublicPriceAmount <= 0 || string.IsNullOrWhiteSpace(ConditionDescription))
            throw new DomainException("listing.content_incomplete", "Заполните цену и прозрачное описание состояния.");
        SnapshotJson = snapshotJson; Status = ListingContentStatus.Ready; ReadyAt = now;
        AddHistory(commandId, "Ready", snapshotJson, actorUserId, now); Touch(now); return true;
    }

    public bool Export(Guid commandId, string? channel, Guid actorUserId, DateTimeOffset now)
    {
        var normalized = Require(channel, 100, "Канал обязателен.");
        if (EnsureCommand(commandId, "Exported", normalized)) return false;
        EnsureReady();
        var publication = FindOrCreatePublication(normalized, now);
        publication.Export(now);
        AddHistory(commandId, "Exported", normalized, actorUserId, now); Touch(now); return true;
    }

    public bool Publish(Guid commandId, string? channel, string? externalId, string? externalUrl,
        Guid actorUserId, DateTimeOffset now)
    {
        var normalized = Require(channel, 100, "Канал обязателен.");
        var signature = JsonSerializer.Serialize(new { normalized, externalId, externalUrl });
        if (EnsureCommand(commandId, "Published", signature)) return false;
        EnsureReady();
        var publication = _publications.SingleOrDefault(x => x.Channel == normalized)
            ?? throw new DomainException("listing.export_required", "Сначала выполните manual export для канала.");
        publication.Publish(externalId, externalUrl, now);
        AddHistory(commandId, "Published", signature, actorUserId, now); Touch(now); return true;
    }

    public bool Fail(Guid commandId, string? channel, string? error, Guid actorUserId, DateTimeOffset now)
    {
        var normalized = Require(channel, 100, "Канал обязателен.");
        var message = Require(error, 2000, "Описание ошибки обязательно.");
        var signature = JsonSerializer.Serialize(new { normalized, message });
        if (EnsureCommand(commandId, "Failed", signature)) return false;
        EnsureReady(); FindOrCreatePublication(normalized, now).Fail(message, now);
        AddHistory(commandId, "Failed", signature, actorUserId, now); Touch(now); return true;
    }

    public bool Unpublish(Guid commandId, string? channel, Guid actorUserId, DateTimeOffset now)
    {
        var normalized = Require(channel, 100, "Канал обязателен.");
        if (EnsureCommand(commandId, "Unpublished", normalized)) return false;
        EnsureReady();
        var publication = _publications.SingleOrDefault(x => x.Channel == normalized)
            ?? throw new DomainException("listing.publication_missing", "Публикация канала не найдена.");
        publication.Unpublish(now); AddHistory(commandId, "Unpublished", normalized, actorUserId, now);
        Touch(now); return true;
    }

    private ChannelPublication FindOrCreatePublication(string channel, DateTimeOffset now)
    {
        var publication = _publications.SingleOrDefault(x => x.Channel == channel);
        if (publication is not null) return publication;
        publication = new ChannelPublication(Guid.NewGuid(), OrganizationId, Id, channel, now);
        _publications.Add(publication); return publication;
    }
    private bool EnsureCommand(Guid commandId, string action, string signature)
    {
        var existing = _history.SingleOrDefault(x => x.CommandId == commandId);
        if (existing is null) return false;
        if (existing.Action != action || existing.Signature != signature)
            throw new ConflictException("listing.command_id_conflict", "Command ID уже использован с другим payload.");
        return true;
    }
    private void AddHistory(Guid commandId, string action, string signature, Guid actorUserId, DateTimeOffset now) =>
        _history.Add(new ListingContentHistory(Guid.NewGuid(), OrganizationId, Id, commandId, action, signature,
            PublicPriceAmount, Currency, actorUserId, now));
    private void EnsureDraft(long expectedVersion)
    {
        if (Version != expectedVersion) throw new ConflictException("listing.version_conflict", "Listing изменён конкурентно.");
        if (Status != ListingContentStatus.Draft) throw new DomainException("listing.immutable", "Listing Ready неизменяем; создайте новую ревизию.");
    }
    private void EnsureReady()
    {
        if (Status != ListingContentStatus.Ready) throw new DomainException("listing.not_ready", "Listing ещё не готов к экспорту.");
    }
    private void Touch(DateTimeOffset now) { UpdatedAt = now; Version++; }
    private static string Require(string? value, int max, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("listing.required", message);
        var result = value.Trim(); if (result.Length > max) throw new DomainException("listing.too_long", $"Максимальная длина — {max} символов."); return result;
    }
    private static string Normalize(string? value, int max) => string.IsNullOrWhiteSpace(value) ? string.Empty : Require(value, max, string.Empty);
}

public sealed class ChannelPublication
{
    private ChannelPublication() { }
    internal ChannelPublication(Guid id, Guid organizationId, Guid listingContentId, string channel, DateTimeOffset now)
    { Id = id; OrganizationId = organizationId; ListingContentId = listingContentId; Channel = channel; Status = ChannelPublicationStatus.Draft; CreatedAt = now; }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ListingContentId { get; private set; }
    public string Channel { get; private set; } = string.Empty;
    public ChannelPublicationStatus Status { get; private set; }
    public string? ExternalId { get; private set; }
    public string? ExternalUrl { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExportedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? UnpublishedAt { get; private set; }
    internal void Export(DateTimeOffset now) { Status = ChannelPublicationStatus.Exported; ExportedAt = now; Error = null; }
    internal void Publish(string? externalId, string? externalUrl, DateTimeOffset now)
    {
        if (Status is not (ChannelPublicationStatus.Exported or ChannelPublicationStatus.Failed))
            throw new DomainException("listing.publish_invalid_status", "Ручное подтверждение доступно после export.");
        Status = ChannelPublicationStatus.Published; ExternalId = Normalize(externalId, 200); ExternalUrl = Normalize(externalUrl, 1000); PublishedAt = now; Error = null;
    }
    internal void Fail(string error, DateTimeOffset now) { Status = ChannelPublicationStatus.Failed; Error = error; ExportedAt ??= now; }
    internal void Unpublish(DateTimeOffset now)
    {
        if (Status != ChannelPublicationStatus.Published) throw new DomainException("listing.not_published", "Канал не опубликован.");
        Status = ChannelPublicationStatus.Unpublished; UnpublishedAt = now;
    }
    private static string? Normalize(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null
        : value.Trim().Length <= max ? value.Trim() : throw new DomainException("listing.too_long", $"Максимальная длина — {max} символов.");
}

public sealed class ListingContentHistory
{
    private ListingContentHistory() { }
    internal ListingContentHistory(Guid id, Guid organizationId, Guid listingContentId, Guid commandId,
        string action, string signature, decimal publicPriceAmount, string currency, Guid actorUserId,
        DateTimeOffset occurredAt)
    { Id = id; OrganizationId = organizationId; ListingContentId = listingContentId; CommandId = commandId; Action = action; Signature = signature; PublicPriceAmount = publicPriceAmount; Currency = currency; ActorUserId = actorUserId; OccurredAt = occurredAt; }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ListingContentId { get; private set; }
    public Guid CommandId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Signature { get; private set; } = string.Empty;
    public decimal PublicPriceAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
