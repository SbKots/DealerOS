namespace DealerOS.SharedKernel;

public sealed class NotFoundException(string message) : Exception(message);
