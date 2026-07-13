using DealerOS.SharedKernel;

namespace DealerOS.Modules.Inspections.Domain;

public sealed class Inspection
{
    private readonly List<InspectionItem> _items = [];
    private readonly List<InspectionDefect> _defects = [];
    private Inspection() { }

    private Inspection(Guid id, Guid organizationId, Guid branchId, Guid vehicleId, Guid inspectorId,
        InspectionTemplate template, int mileageKm, DateTimeOffset now, Guid? correctsInspectionId, int revision)
    {
        if (mileageKm < 0 || mileageKm > 3_000_000)
            throw new DomainException("inspection.invalid_mileage", "Пробег на момент осмотра находится вне допустимого диапазона.");

        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        VehicleId = vehicleId;
        InspectorId = inspectorId;
        Status = InspectionStatus.Draft;
        MileageKm = mileageKm;
        TemplateId = template.Id;
        TemplateName = template.Name;
        TemplateVersion = template.Version;
        CorrectsInspectionId = correctsInspectionId;
        Revision = revision;
        Version = 1;
        CreatedAt = now;
        UpdatedAt = now;

        foreach (var source in template.Items.OrderBy(x => x.SortOrder))
        {
            _items.Add(InspectionItem.FromTemplate(Guid.NewGuid(), organizationId, id, source, now));
        }
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid InspectorId { get; private set; }
    public InspectionStatus Status { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int MileageKm { get; private set; }
    public string? FinalComment { get; private set; }
    public Guid TemplateId { get; private set; }
    public string TemplateName { get; private set; } = string.Empty;
    public int TemplateVersion { get; private set; }
    public Guid? CorrectsInspectionId { get; private set; }
    public int Revision { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<InspectionItem> Items => _items;
    public IReadOnlyCollection<InspectionDefect> Defects => _defects;
    public bool NeedsReconditioning => _defects.Any(x => x.RepairRequired || x.BlocksSale);

    public static Inspection CreateDraft(Guid organizationId, Guid branchId, Guid vehicleId, Guid inspectorId,
        InspectionTemplate template, int mileageKm, DateTimeOffset now) =>
        new(Guid.NewGuid(), organizationId, branchId, vehicleId, inspectorId, template, mileageKm, now, null, 1);

    public static Inspection CreateCorrection(Inspection source, Guid inspectorId, DateTimeOffset now)
    {
        if (source.Status != InspectionStatus.Completed)
            throw new DomainException("inspection.correction_requires_completed", "Корректировку можно создать только для завершённого осмотра.");

        var correction = new Inspection
        {
            Id = Guid.NewGuid(),
            OrganizationId = source.OrganizationId,
            BranchId = source.BranchId,
            VehicleId = source.VehicleId,
            InspectorId = inspectorId,
            Status = InspectionStatus.Draft,
            MileageKm = source.MileageKm,
            TemplateId = source.TemplateId,
            TemplateName = source.TemplateName,
            TemplateVersion = source.TemplateVersion,
            CorrectsInspectionId = source.Id,
            Revision = source.Revision + 1,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        foreach (var item in source.Items.OrderBy(x => x.SortOrder)) correction._items.Add(item.CopyTo(correction.Id, now));
        foreach (var defect in source.Defects) correction._defects.Add(defect.CopyTo(correction.Id, now, inspectorId));
        return correction;
    }

    public void Start(DateTimeOffset now)
    {
        if (Status != InspectionStatus.Draft)
            throw new DomainException("inspection.invalid_transition", "Начать можно только черновик осмотра.");
        Status = InspectionStatus.InProgress;
        StartedAt = now;
        Touch(now);
    }

    public void SaveItem(Guid itemId, InspectionItemResult result, string? comment, long expectedVersion, DateTimeOffset now)
    {
        EnsureEditable(expectedVersion);
        var item = _items.SingleOrDefault(x => x.Id == itemId)
            ?? throw new NotFoundException("Пункт осмотра не найден.");
        item.Save(result, comment, now);
        Touch(now);
    }

    public InspectionDefect AddDefect(Guid defectId, InspectionCategory category, string? title, string? description,
        DefectSeverity severity, string? recommendation, decimal? estimatedRepairAmount, string? currency,
        bool repairRequired, bool blocksPublication, bool blocksTestDrive, bool blocksSale,
        Guid actorUserId, long expectedVersion, DateTimeOffset now)
    {
        EnsureEditable(expectedVersion);
        var existing = _defects.SingleOrDefault(x => x.Id == defectId);
        if (existing is not null) return existing;
        var defect = new InspectionDefect(defectId, OrganizationId, Id, category, title, description, severity,
            recommendation, estimatedRepairAmount, currency, repairRequired, blocksPublication, blocksTestDrive,
            blocksSale, actorUserId, now);
        _defects.Add(defect);
        Touch(now);
        return defect;
    }

    public void ValidateEditable(long expectedVersion) => EnsureEditable(expectedVersion);

    public InspectionPhoto AddPhoto(Guid defectId, Guid photoId, string originalFileName, string objectKey,
        string contentType, long sizeBytes, Guid actorUserId, long expectedVersion, DateTimeOffset now)
    {
        EnsureEditable(expectedVersion);
        var defect = _defects.SingleOrDefault(x => x.Id == defectId)
            ?? throw new NotFoundException("Дефект не найден.");
        var photo = defect.AddPhoto(photoId, originalFileName, objectKey, contentType, sizeBytes, actorUserId, now);
        Touch(now);
        return photo;
    }

    public IReadOnlyList<string> RemoveDraftDefect(Guid defectId, long expectedVersion, DateTimeOffset now)
    {
        EnsureEditable(expectedVersion);
        var defect = _defects.SingleOrDefault(x => x.Id == defectId);
        if (defect is null) return [];
        var keys = defect.Photos.Select(x => x.ObjectKey).ToArray();
        _defects.Remove(defect);
        Touch(now);
        return keys;
    }

    public bool Complete(string? finalComment, long expectedVersion, DateTimeOffset now)
    {
        if (Status == InspectionStatus.Completed) return NeedsReconditioning;
        EnsureEditable(expectedVersion);
        var missing = _items.Where(x => x.IsRequired && x.Result == InspectionItemResult.Pending).ToArray();
        if (missing.Length > 0)
            throw new DomainException("inspection.required_items_incomplete", "Заполните все обязательные пункты чек-листа.");
        FinalComment = string.IsNullOrWhiteSpace(finalComment) ? null : RequireText(finalComment, 4000);
        Status = InspectionStatus.Completed;
        CompletedAt = now;
        Touch(now);
        return NeedsReconditioning;
    }

    public void Cancel(long expectedVersion, DateTimeOffset now)
    {
        if (Status == InspectionStatus.Cancelled) return;
        if (Status == InspectionStatus.Completed)
            throw new DomainException("inspection.completed_immutable", "Завершённый осмотр нельзя отменить.");
        EnsureVersion(expectedVersion);
        Status = InspectionStatus.Cancelled;
        Touch(now);
    }

    private void EnsureEditable(long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status == InspectionStatus.Completed)
            throw new DomainException("inspection.completed_immutable", "Завершённый осмотр нельзя изменить.");
        if (Status != InspectionStatus.InProgress)
            throw new DomainException("inspection.not_editable", "Изменять можно только выполняемый осмотр.");
    }

    private void EnsureVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
            throw new ConflictException("inspection.version_conflict", "Осмотр был изменён другим пользователем. Обновите данные.");
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    private static string RequireText(string value, int maxLength)
    {
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new DomainException("inspection.field_too_long", $"Поле не должно превышать {maxLength} символов.");
        return normalized;
    }
}

public sealed class InspectionItem
{
    private InspectionItem() { }
    private InspectionItem(Guid id, Guid organizationId, Guid inspectionId, string key, InspectionCategory category,
        string label, string? description, bool isRequired, int sortOrder, InspectionItemResult result,
        string? comment, DateTimeOffset updatedAt)
    {
        Id = id;
        OrganizationId = organizationId;
        InspectionId = inspectionId;
        Key = key;
        Category = category;
        Label = label;
        Description = description;
        IsRequired = isRequired;
        SortOrder = sortOrder;
        Result = result;
        Comment = comment;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid InspectionId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public InspectionCategory Category { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsRequired { get; private set; }
    public int SortOrder { get; private set; }
    public InspectionItemResult Result { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    internal static InspectionItem FromTemplate(Guid id, Guid organizationId, Guid inspectionId,
        InspectionTemplateItem source, DateTimeOffset now) => new(id, organizationId, inspectionId, source.Key,
        source.Category, source.Label, source.Description, source.IsRequired, source.SortOrder,
        InspectionItemResult.Pending, null, now);

    internal InspectionItem CopyTo(Guid inspectionId, DateTimeOffset now) => new(Guid.NewGuid(), OrganizationId,
        inspectionId, Key, Category, Label, Description, IsRequired, SortOrder, Result, Comment, now);

    internal void Save(InspectionItemResult result, string? comment, DateTimeOffset now)
    {
        if (!Enum.IsDefined(result)) throw new DomainException("inspection_item.invalid_result", "Недопустимый результат пункта.");
        Result = result;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (Comment?.Length > 2000) throw new DomainException("inspection_item.comment_too_long", "Комментарий не должен превышать 2000 символов.");
        UpdatedAt = now;
    }
}

public sealed class InspectionDefect
{
    private readonly List<InspectionPhoto> _photos = [];
    private InspectionDefect() { }
    internal InspectionDefect(Guid id, Guid organizationId, Guid inspectionId, InspectionCategory category,
        string? title, string? description, DefectSeverity severity, string? recommendation,
        decimal? estimatedRepairAmount, string? currency, bool repairRequired, bool blocksPublication,
        bool blocksTestDrive, bool blocksSale, Guid createdByUserId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new DomainException("defect.id_required", "Идентификатор дефекта обязателен.");
        if (!Enum.IsDefined(category) || !Enum.IsDefined(severity))
            throw new DomainException("defect.invalid_classification", "Категория или серьёзность дефекта недопустима.");
        Id = id;
        OrganizationId = organizationId;
        InspectionId = inspectionId;
        Category = category;
        Title = RequireText(title, 300, "Название дефекта обязательно.");
        Description = RequireText(description, 4000, "Описание дефекта обязательно.");
        Severity = severity;
        Recommendation = string.IsNullOrWhiteSpace(recommendation) ? null : RequireText(recommendation, 4000, string.Empty);
        if (estimatedRepairAmount is < 0 or > Money.MaxAmount)
            throw new DomainException("defect.invalid_amount", "Стоимость устранения находится вне допустимого диапазона.");
        EstimatedRepairAmount = estimatedRepairAmount is null ? null : decimal.Round(estimatedRepairAmount.Value, 2, MidpointRounding.ToEven);
        Currency = estimatedRepairAmount is null ? null : new Money(Math.Max(0.01m, estimatedRepairAmount.Value), currency).Currency;
        RepairRequired = repairRequired || severity == DefectSeverity.Critical;
        BlocksPublication = blocksPublication;
        BlocksTestDrive = blocksTestDrive;
        BlocksSale = blocksSale || severity == DefectSeverity.Critical;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid InspectionId { get; private set; }
    public InspectionCategory Category { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DefectSeverity Severity { get; private set; }
    public string? Recommendation { get; private set; }
    public decimal? EstimatedRepairAmount { get; private set; }
    public string? Currency { get; private set; }
    public bool RepairRequired { get; private set; }
    public bool BlocksPublication { get; private set; }
    public bool BlocksTestDrive { get; private set; }
    public bool BlocksSale { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<InspectionPhoto> Photos => _photos;

    internal InspectionPhoto AddPhoto(Guid photoId, string originalFileName, string objectKey, string contentType,
        long sizeBytes, Guid actorUserId, DateTimeOffset now)
    {
        var existing = _photos.SingleOrDefault(x => x.Id == photoId);
        if (existing is not null) return existing;
        var photo = new InspectionPhoto(photoId, OrganizationId, Id, originalFileName, objectKey, contentType,
            sizeBytes, actorUserId, now);
        _photos.Add(photo);
        return photo;
    }

    internal InspectionDefect CopyTo(Guid inspectionId, DateTimeOffset now, Guid actorUserId)
    {
        var copy = new InspectionDefect(Guid.NewGuid(), OrganizationId, inspectionId, Category, Title, Description,
            Severity, Recommendation, EstimatedRepairAmount, Currency, RepairRequired, BlocksPublication,
            BlocksTestDrive, BlocksSale, actorUserId, now);
        foreach (var photo in _photos) copy._photos.Add(photo.CopyTo(copy.Id));
        return copy;
    }

    private static string RequireText(string? value, int maxLength, string requiredMessage)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("defect.required_field", requiredMessage);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new DomainException("defect.field_too_long", $"Поле не должно превышать {maxLength} символов.");
        return normalized;
    }
}

public sealed class InspectionPhoto
{
    private InspectionPhoto() { }
    internal InspectionPhoto(Guid id, Guid organizationId, Guid defectId, string originalFileName, string objectKey,
        string contentType, long sizeBytes, Guid createdByUserId, DateTimeOffset createdAt,
        Guid? sourcePhotoId = null)
    {
        if (id == Guid.Empty) throw new DomainException("inspection_photo.id_required", "Идентификатор фотографии обязателен.");
        Id = id;
        OrganizationId = organizationId;
        DefectId = defectId;
        OriginalFileName = Path.GetFileName(originalFileName.Trim());
        if (OriginalFileName.Length is 0 or > 255) throw new DomainException("inspection_photo.invalid_name", "Имя файла недопустимо.");
        ObjectKey = objectKey;
        ContentType = contentType;
        if (sizeBytes <= 0) throw new DomainException("inspection_photo.invalid_size", "Фотография пуста.");
        SizeBytes = sizeBytes;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        SourcePhotoId = sourcePhotoId;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid DefectId { get; private set; }
    public Guid? SourcePhotoId { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ObjectKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    internal InspectionPhoto CopyTo(Guid defectId) => new(Guid.NewGuid(), OrganizationId, defectId,
        OriginalFileName, ObjectKey, ContentType, SizeBytes, CreatedByUserId, CreatedAt, SourcePhotoId ?? Id);
}
