namespace DealerOS.SharedKernel;

public sealed class ConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
