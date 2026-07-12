namespace DealerOS.SharedKernel;

public sealed class DomainException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
