using DealerOS.Modules.Deals.Application;
using DealerOS.SharedKernel;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace DealerOS.Api.Infrastructure;

public sealed class MinioDealDocumentStorage(IMinioClient client, IConfiguration configuration,
    ILogger<MinioDealDocumentStorage> logger) : IDealDocumentStorage
{
    private readonly string _bucket = configuration["ObjectStorage:Bucket"] ?? "dealeros-private";

    public async Task PutAsync(string objectKey, Stream content, long sizeBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.PutObjectAsync(new PutObjectArgs().WithBucket(_bucket).WithObject(objectKey)
                .WithStreamData(content).WithObjectSize(sizeBytes).WithContentType("application/pdf"),
                cancellationToken);
        }
        catch (Exception exception) when (IsStorageException(exception))
        { throw new StorageUnavailableException("Не удалось сохранить документ сделки.", exception); }
    }

    public async Task<Stream> GetAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            var output = new MemoryStream();
            await client.GetObjectAsync(new GetObjectArgs().WithBucket(_bucket).WithObject(objectKey)
                .WithCallbackStream(async (stream, ct) => await stream.CopyToAsync(output, ct)), cancellationToken);
            output.Position = 0; return output;
        }
        catch (Exception exception) when (IsStorageException(exception))
        { throw new StorageUnavailableException("Не удалось скачать документ сделки.", exception); }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        { await client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(_bucket).WithObject(objectKey), cancellationToken); }
        catch (Exception exception) when (IsStorageException(exception))
        {
            logger.LogWarning(exception, "Failed to remove deal document object {ObjectKey}", objectKey);
            throw new StorageUnavailableException("Не удалось удалить временный документ сделки.", exception);
        }
    }

    private static bool IsStorageException(Exception exception) => exception is MinioException
        or HttpRequestException or IOException or TaskCanceledException;
}
