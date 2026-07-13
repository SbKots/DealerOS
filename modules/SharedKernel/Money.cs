namespace DealerOS.SharedKernel;

public readonly record struct Money
{
    public const decimal MaxAmount = 99_999_999_999_999_999.99m;
    private static readonly HashSet<string> SupportedCurrencies = ["RUB", "USD", "EUR", "CNY"];

    public Money(decimal amount, string? currency)
    {
        if (amount < 0)
        {
            throw new DomainException("money.negative", "Сумма не может быть отрицательной.");
        }

        if (amount > MaxAmount)
        {
            throw new DomainException("money.too_large", "Сумма превышает допустимый предел.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainException("money.currency_required", "Валюта обязательна.");
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (!SupportedCurrencies.Contains(normalizedCurrency))
        {
            throw new DomainException("money.currency_not_supported", "Валюта не поддерживается.");
        }

        Amount = decimal.Round(amount, 2, MidpointRounding.ToEven);
        Currency = normalizedCurrency;
    }

    public decimal Amount { get; }
    public string Currency { get; }
}
