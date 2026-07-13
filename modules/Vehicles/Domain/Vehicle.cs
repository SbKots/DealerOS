using DealerOS.SharedKernel;

namespace DealerOS.Modules.Vehicles.Domain;

public sealed class Vehicle
{
    private readonly List<VehicleStatusHistory> _statusHistory = [];
    private Vehicle() { }

    private Vehicle(Guid id, Guid organizationId, Guid branchId, Vin vin, string? make, string? model, int year,
        int mileageKm, Money plannedPurchasePrice, DateTimeOffset now, Guid actorUserId)
    {
        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        Vin = vin.Value;
        Make = RequireText(make, 100, "Марка обязательна.", "Марка не должна превышать 100 символов.");
        Model = RequireText(model, 100, "Модель обязательна.", "Модель не должна превышать 100 символов.");
        if (year < 1950 || year > now.Year + 1)
        {
            throw new DomainException("vehicle.invalid_year", "Год выпуска находится вне допустимого диапазона.");
        }
        if (mileageKm < 0 || mileageKm > 3_000_000)
        {
            throw new DomainException("vehicle.invalid_mileage", "Пробег находится вне допустимого диапазона.");
        }
        if (plannedPurchasePrice.Amount <= 0)
        {
            throw new DomainException("vehicle.purchase_price_required", "Плановая цена закупки должна быть больше нуля.");
        }

        Year = year;
        MileageKm = mileageKm;
        PlannedPurchaseAmount = plannedPurchasePrice.Amount;
        Currency = plannedPurchasePrice.Currency;
        Status = VehicleStatus.IntakeDraft;
        CreatedAt = now;
        CreatedByUserId = actorUserId;
        Version = 1;
        _statusHistory.Add(new VehicleStatusHistory(Guid.NewGuid(), id, organizationId, null, Status, now, actorUserId));
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Vin { get; private set; } = string.Empty;
    public string Make { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public int Year { get; private set; }
    public int MileageKm { get; private set; }
    public decimal PlannedPurchaseAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public VehicleStatus Status { get; private set; }
    public string? StockNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<VehicleStatusHistory> StatusHistory => _statusHistory;

    public static Vehicle CreateDraft(Guid organizationId, Guid branchId, string? vin, string? make, string? model,
        int year, int mileageKm, Money plannedPurchasePrice, DateTimeOffset now, Guid actorUserId) =>
        new(Guid.NewGuid(), organizationId, branchId, new Vin(vin), make, model, year, mileageKm, plannedPurchasePrice, now, actorUserId);

    public void AcceptToStock(string branchCode, DateTimeOffset now, Guid actorUserId)
    {
        if (Status != VehicleStatus.IntakeDraft)
        {
            throw new DomainException("vehicle.invalid_transition", "На склад можно принять только черновик поступления.");
        }

        var previous = Status;
        Status = VehicleStatus.InStock;
        AcceptedAt = now;
        StockNumber = $"{branchCode.ToUpperInvariant()}-{now:yyyy}-{Id.ToString("N")[..8].ToUpperInvariant()}";
        Version++;
        _statusHistory.Add(new VehicleStatusHistory(Guid.NewGuid(), Id, OrganizationId, previous, Status, now, actorUserId));
    }

    public void BeginInspection(DateTimeOffset now, Guid actorUserId)
    {
        if (Status != VehicleStatus.InStock)
            throw new DomainException("vehicle.inspection_requires_in_stock", "Начать осмотр можно только для автомобиля на складе.");
        TransitionTo(VehicleStatus.InspectionInProgress, now, actorUserId);
    }

    public void BeginInspectionCorrection(DateTimeOffset now, Guid actorUserId)
    {
        if (Status is not (VehicleStatus.ReconditioningRequired or VehicleStatus.InspectionPassed))
            throw new DomainException("vehicle.correction_invalid_status", "Корректировка недоступна для текущего статуса автомобиля.");
        TransitionTo(VehicleStatus.InspectionInProgress, now, actorUserId);
    }

    public void CompleteInspection(bool needsReconditioning, DateTimeOffset now, Guid actorUserId)
    {
        if (Status != VehicleStatus.InspectionInProgress)
            throw new DomainException("vehicle.inspection_not_in_progress", "У автомобиля нет выполняемого осмотра.");
        TransitionTo(needsReconditioning ? VehicleStatus.ReconditioningRequired : VehicleStatus.InspectionPassed,
            now, actorUserId);
    }

    public void CancelInspection(DateTimeOffset now, Guid actorUserId)
    {
        if (Status != VehicleStatus.InspectionInProgress)
            throw new DomainException("vehicle.inspection_not_in_progress", "У автомобиля нет выполняемого осмотра.");
        TransitionTo(VehicleStatus.InStock, now, actorUserId);
    }

    public void MarkReadyForSale(DateTimeOffset now, Guid actorUserId)
    {
        if (Status == VehicleStatus.ReadyForSale) return;
        if (Status != VehicleStatus.ReconditioningRequired)
            throw new DomainException("vehicle.ready_requires_reconditioning",
                "ReadyForSale устанавливается только после подготовки и успешного контроля качества.");
        TransitionTo(VehicleStatus.ReadyForSale, now, actorUserId);
    }

    private void TransitionTo(VehicleStatus next, DateTimeOffset now, Guid actorUserId)
    {
        var previous = Status;
        Status = next;
        Version++;
        _statusHistory.Add(new VehicleStatusHistory(Guid.NewGuid(), Id, OrganizationId, previous, next, now, actorUserId));
    }

    private static string RequireText(string? value, int maxLength, string requiredMessage, string lengthMessage)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("vehicle.required_field", requiredMessage);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new DomainException("vehicle.field_too_long", lengthMessage);
        return normalized;
    }
}

public sealed class VehicleStatusHistory
{
    private VehicleStatusHistory() { }
    public VehicleStatusHistory(Guid id, Guid vehicleId, Guid organizationId, VehicleStatus? fromStatus,
        VehicleStatus toStatus, DateTimeOffset changedAt, Guid changedByUserId)
    {
        Id = id;
        VehicleId = vehicleId;
        OrganizationId = organizationId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedAt = changedAt;
        ChangedByUserId = changedByUserId;
    }

    public Guid Id { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public VehicleStatus? FromStatus { get; private set; }
    public VehicleStatus ToStatus { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public Guid ChangedByUserId { get; private set; }
}
