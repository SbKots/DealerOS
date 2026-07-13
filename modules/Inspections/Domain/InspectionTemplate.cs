using DealerOS.SharedKernel;

namespace DealerOS.Modules.Inspections.Domain;

public sealed class InspectionTemplate
{
    private readonly List<InspectionTemplateItem> _items = [];
    private InspectionTemplate() { }

    public InspectionTemplate(Guid id, Guid organizationId, string? name, int version,
        IEnumerable<InspectionTemplateItemDefinition> items, DateTimeOffset createdAt, Guid createdByUserId)
    {
        if (id == Guid.Empty || organizationId == Guid.Empty || createdByUserId == Guid.Empty)
            throw new DomainException("inspection_template.invalid_identity", "Идентификаторы шаблона обязательны.");
        Name = RequireText(name, 200, "Название шаблона обязательно.");
        if (version < 1) throw new DomainException("inspection_template.invalid_version", "Версия шаблона должна быть положительной.");

        Id = id;
        OrganizationId = organizationId;
        Version = version;
        CreatedAt = createdAt;
        CreatedByUserId = createdByUserId;

        var definitions = items.OrderBy(x => x.SortOrder).ToArray();
        if (definitions.Length == 0) throw new DomainException("inspection_template.items_required", "Шаблон должен содержать пункты.");
        if (definitions.Select(x => x.Key.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definitions.Length)
            throw new DomainException("inspection_template.duplicate_key", "Ключи пунктов шаблона должны быть уникальными.");

        foreach (var definition in definitions)
        {
            _items.Add(new InspectionTemplateItem(Guid.NewGuid(), organizationId, id, definition.Key,
                definition.Category, definition.Label, definition.Description, definition.IsRequired, definition.SortOrder));
        }
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public IReadOnlyCollection<InspectionTemplateItem> Items => _items;

    private static string RequireText(string? value, int maxLength, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("inspection_template.required_field", message);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new DomainException("inspection_template.field_too_long", $"Поле не должно превышать {maxLength} символов.");
        return normalized;
    }
}

public sealed class InspectionTemplateItem
{
    private InspectionTemplateItem() { }
    internal InspectionTemplateItem(Guid id, Guid organizationId, Guid templateId, string? key,
        InspectionCategory category, string? label, string? description, bool isRequired, int sortOrder)
    {
        Id = id;
        OrganizationId = organizationId;
        TemplateId = templateId;
        Key = RequireText(key, 100);
        if (!Enum.IsDefined(category))
            throw new DomainException("inspection_template.invalid_category", "Категория пункта шаблона недопустима.");
        Category = category;
        Label = RequireText(label, 300);
        Description = string.IsNullOrWhiteSpace(description) ? null : RequireText(description, 1000);
        IsRequired = isRequired;
        if (sortOrder < 0) throw new DomainException("inspection_template.invalid_sort_order", "Порядок пункта не может быть отрицательным.");
        SortOrder = sortOrder;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid TemplateId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public InspectionCategory Category { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsRequired { get; private set; }
    public int SortOrder { get; private set; }

    private static string RequireText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("inspection_template.required_field", "Поле пункта шаблона обязательно.");
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new DomainException("inspection_template.field_too_long", $"Поле не должно превышать {maxLength} символов.");
        return normalized;
    }
}

public sealed record InspectionTemplateItemDefinition(string Key, InspectionCategory Category, string Label,
    string? Description, bool IsRequired, int SortOrder);
