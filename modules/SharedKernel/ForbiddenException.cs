namespace DealerOS.SharedKernel;

public sealed class ForbiddenException(string message) : Exception(message);
