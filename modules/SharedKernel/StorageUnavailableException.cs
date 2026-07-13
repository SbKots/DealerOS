namespace DealerOS.SharedKernel;

public sealed class StorageUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
