using System.Text.RegularExpressions;
using DealerOS.SharedKernel;

namespace DealerOS.Modules.Vehicles.Domain;

public readonly partial record struct Vin
{
    public Vin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("vehicle.vin_required", "VIN обязателен.");
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (!VinPattern().IsMatch(normalized))
        {
            throw new DomainException("vehicle.invalid_vin", "VIN должен содержать 17 допустимых символов без I, O и Q.");
        }

        Value = normalized;
    }

    public string Value { get; }

    [GeneratedRegex("^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.CultureInvariant)]
    private static partial Regex VinPattern();

    public override string ToString() => Value;
}
