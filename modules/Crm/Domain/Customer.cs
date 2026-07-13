using System.Text.RegularExpressions;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Crm.Domain;

public enum CustomerType { Individual = 1, LegalEntity = 2 }
public enum PreferredContactChannel { Phone = 1, Email = 2, Messenger = 3 }

public sealed partial class Customer
{
    private Customer() { }
    private Customer(Guid organizationId, Guid createdInBranchId, CustomerType type, string? name, string? phone,
        string? email, PreferredContactChannel preferredChannel, bool consentGiven, bool marketingConsent,
        DateTimeOffset? consentAt, string? consentSource, Guid actorUserId, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); OrganizationId = organizationId; CreatedInBranchId = createdInBranchId; Type = type;
        Name = Require(name, 300, "Имя или наименование обязательно.");
        NormalizedPhone = NormalizePhone(phone); NormalizedEmail = NormalizeEmail(email);
        if (NormalizedPhone is null && NormalizedEmail is null)
            throw new DomainException("crm.contact_required", "Укажите телефон или email.");
        PreferredChannel = preferredChannel; ConsentGiven = consentGiven; MarketingConsent = marketingConsent;
        if ((consentGiven || marketingConsent) && (consentAt is null || string.IsNullOrWhiteSpace(consentSource)))
            throw new DomainException("crm.consent_evidence_required", "Для согласия обязательны дата и источник.");
        ConsentAt = consentAt; ConsentSource = Normalize(consentSource, 200); CreatedByUserId = actorUserId;
        CreatedAt = now; UpdatedAt = now; Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid CreatedInBranchId { get; private set; }
    public CustomerType Type { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NormalizedPhone { get; private set; }
    public string? NormalizedEmail { get; private set; }
    public PreferredContactChannel PreferredChannel { get; private set; }
    public bool ConsentGiven { get; private set; }
    public bool MarketingConsent { get; private set; }
    public DateTimeOffset? ConsentAt { get; private set; }
    public string? ConsentSource { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? MergedIntoCustomerId { get; private set; }
    public Guid? MergedByUserId { get; private set; }
    public Guid? MergeCommandId { get; private set; }
    public string? MergeReason { get; private set; }
    public DateTimeOffset? MergedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }
    public bool IsMerged => MergedIntoCustomerId is not null;

    public static Customer Create(Guid organizationId, Guid branchId, CustomerType type, string? name,
        string? phone, string? email, PreferredContactChannel preferredChannel, bool consentGiven,
        bool marketingConsent, DateTimeOffset? consentAt, string? consentSource, Guid actorUserId,
        DateTimeOffset now) => new(organizationId, branchId, type, name, phone, email, preferredChannel,
            consentGiven, marketingConsent, consentAt, consentSource, actorUserId, now);

    public bool MergeInto(Guid commandId, Guid targetCustomerId, Guid actorUserId, string? reason, long expectedVersion,
        DateTimeOffset now)
    {
        var normalizedReason = Require(reason, 2000, "Причина объединения обязательна.");
        if (MergeCommandId == commandId)
        {
            if (MergedIntoCustomerId != targetCustomerId || MergeReason != normalizedReason)
                throw new ConflictException("crm.merge_command_conflict", "Command ID объединения использован с другим payload.");
            return false;
        }
        EnsureVersion(expectedVersion);
        if (IsMerged) throw new DomainException("crm.customer_already_merged", "Customer уже объединён.");
        if (targetCustomerId == Id) throw new DomainException("crm.customer_merge_self", "Нельзя объединить Customer с собой.");
        MergedIntoCustomerId = targetCustomerId; MergedByUserId = actorUserId; MergeCommandId = commandId;
        MergeReason = normalizedReason; MergedAt = now; Touch(now); return true;
    }
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = NonDigits().Replace(value, string.Empty);
        if (digits.Length is < 7 or > 15) throw new DomainException("crm.invalid_phone", "Телефон должен содержать 7–15 цифр.");
        if (digits.Length == 11 && digits[0] == '8') digits = $"7{digits[1..]}";
        return $"+{digits}";
    }
    public static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var email = value.Trim().ToLowerInvariant();
        if (email.Length > 320 || !email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
            throw new DomainException("crm.invalid_email", "Email имеет неверный формат.");
        return email;
    }
    private void EnsureVersion(long expectedVersion)
    { if (Version != expectedVersion) throw new ConflictException("crm.customer_version_conflict", "Customer изменён конкурентно."); }
    private void Touch(DateTimeOffset now) { UpdatedAt = now; Version++; }
    private static string Require(string? value, int max, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("crm.required", message);
        return Normalize(value, max)!;
    }
    private static string? Normalize(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > max) throw new DomainException("crm.too_long", $"Максимальная длина — {max} символов.");
        return normalized;
    }
    [GeneratedRegex("[^0-9]")]
    private static partial Regex NonDigits();
}
