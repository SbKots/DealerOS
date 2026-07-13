using DealerOS.Modules.Vehicles.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.UnitTests;

public sealed class VehicleIntakeDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateDraft_NormalizesVinAndKeepsMoneyAsDecimal()
    {
        var vehicle = Vehicle.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "wvwzzz1jzxw000001", "Volkswagen", "Passat",
            2021, 52_000, new Money(1_850_000.129m, "rub"), Now, Guid.NewGuid());

        Assert.Equal("WVWZZZ1JZXW000001", vehicle.Vin);
        Assert.Equal(1_850_000.13m, vehicle.PlannedPurchaseAmount);
        Assert.Equal("RUB", vehicle.Currency);
        Assert.Equal(VehicleStatus.IntakeDraft, vehicle.Status);
        Assert.Single(vehicle.StatusHistory);
    }

    [Theory]
    [InlineData("SHORTVIN")]
    [InlineData("WVWZZZ1JZXW00000I")]
    [InlineData("WVWZZZ1JZXW00000O")]
    [InlineData("WVWZZZ1JZXW00000Q")]
    public void CreateDraft_RejectsInvalidVin(string vin)
    {
        var exception = Assert.Throws<DomainException>(() => Vehicle.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), vin,
            "Volkswagen", "Passat", 2021, 52_000, new Money(1_850_000m, "RUB"), Now, Guid.NewGuid()));

        Assert.Equal("vehicle.invalid_vin", exception.Code);
    }

    [Fact]
    public void CreateDraft_RejectsMissingVinAndOversizedNames()
    {
        var missingVin = Assert.Throws<DomainException>(() => Vehicle.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), null,
            "Volkswagen", "Passat", 2021, 52_000, new Money(1_850_000m, "RUB"), Now, Guid.NewGuid()));
        Assert.Equal("vehicle.vin_required", missingVin.Code);

        var longMake = Assert.Throws<DomainException>(() => Vehicle.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "WVWZZZ1JZXW000001",
            new string('A', 101), "Passat", 2021, 52_000, new Money(1_850_000m, "RUB"), Now, Guid.NewGuid()));
        Assert.Equal("vehicle.field_too_long", longMake.Code);
    }

    [Theory]
    [InlineData("1.005", "1.00")]
    [InlineData("1.015", "1.02")]
    [InlineData("999.999", "1000.00")]
    public void Money_UsesDocumentedBankersRounding(string input, string expected)
    {
        var money = new Money(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture), "rub");
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), money.Amount);
        Assert.Equal("RUB", money.Currency);
    }

    [Fact]
    public void Money_RejectsMissingCurrencyNegativeAndOverflow()
    {
        Assert.Equal("money.currency_required", Assert.Throws<DomainException>(() => new Money(1m, null)).Code);
        Assert.Equal("money.negative", Assert.Throws<DomainException>(() => new Money(-0.01m, "RUB")).Code);
        Assert.Equal("money.too_large", Assert.Throws<DomainException>(() => new Money(Money.MaxAmount + 0.01m, "RUB")).Code);
    }

    [Fact]
    public void AcceptToStock_PerformsControlledTransitionAndCreatesHistory()
    {
        var actor = Guid.NewGuid();
        var vehicle = Vehicle.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "WVWZZZ1JZXW000001", "Volkswagen", "Passat",
            2021, 52_000, new Money(1_850_000m, "RUB"), Now, actor);

        vehicle.AcceptToStock("msk", Now.AddMinutes(10), actor);

        Assert.Equal(VehicleStatus.InStock, vehicle.Status);
        Assert.StartsWith("MSK-2026-", vehicle.StockNumber);
        Assert.Equal(2, vehicle.StatusHistory.Count);
        Assert.Equal(2, vehicle.Version);
    }

    [Fact]
    public void AcceptToStock_CannotBeRepeated()
    {
        var vehicle = Vehicle.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "WVWZZZ1JZXW000001", "Volkswagen", "Passat",
            2021, 52_000, new Money(1_850_000m, "RUB"), Now, Guid.NewGuid());
        vehicle.AcceptToStock("MSK", Now, Guid.NewGuid());

        var exception = Assert.Throws<DomainException>(() => vehicle.AcceptToStock("MSK", Now, Guid.NewGuid()));

        Assert.Equal("vehicle.invalid_transition", exception.Code);
    }
}
