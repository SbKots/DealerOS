using DealerOS.Modules.Finance.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.UnitTests;

public sealed class FinanceDomainTests
{
    [Fact]
    public void FormulaUsesAuthoritativeRevenueOnceAndCalculatesPlanFact()
    {
        var result = ProfitCalculator.Calculate("RUB", 1_500_000m, 100_000m, 1_000_000m,
            [new(100_000m, "RUB"), new(50_000m, "RUB")], [new(25_000m, "RUB")], 300_000m);

        Assert.Equal(1_400_000m, result.NetRevenue);
        Assert.Equal(1_175_000m, result.TotalCost);
        Assert.Equal(225_000m, result.ActualProfit);
        Assert.Equal(16.07m, result.ActualMarginPercent);
        Assert.Equal(20.00m, result.PlanMarginPercent);
    }

    [Fact]
    public void PaymentAmountsAreNotAnInputAndCannotDoubleCountRevenue()
    {
        var result = ProfitCalculator.Calculate("RUB", 1_500_000m, 0m, 1_000_000m,
            [], [], 500_000m);

        Assert.Equal(1_500_000m, result.GrossRevenue);
        Assert.Equal(500_000m, result.ActualProfit);
    }

    [Fact]
    public void BankersRoundingIsUsedForMoneyAndMargin()
    {
        var result = ProfitCalculator.Calculate("RUB", 3m, 0m, 0.005m, [], [], 0m);

        Assert.Equal(0.00m, result.PurchaseCost);
        Assert.Equal(100.00m, result.ActualMarginPercent);
    }

    [Fact]
    public void MixedCurrencyIdentifiesAndBlocksSource()
    {
        var error = Assert.Throws<DomainException>(() => ProfitCalculator.Calculate("RUB", 100m, 0m, 20m,
            [new(1m, "USD")], [], 10m));

        Assert.Equal("finance.mixed_currency", error.Code);
        Assert.Contains("USD", error.Message);
    }

    [Fact]
    public void NegativeProfitIsValidAndVisible()
    {
        var result = ProfitCalculator.Calculate("RUB", 100m, 0m, 120m, [], [], -10m);

        Assert.Equal(-20m, result.ActualProfit);
        Assert.Equal(-20m, result.ActualMarginPercent);
    }

    [Fact]
    public void RefundCannotExceedCompletedRevenue()
    {
        var error = Assert.Throws<DomainException>(() => ProfitCalculator.Calculate("RUB", 100m, 101m, 0m,
            [], [], 0m));

        Assert.Equal("finance.refunds_exceed_revenue", error.Code);
    }
}
