using DealerOS.Modules.Inspections.Application;
using DealerOS.SharedKernel;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace DealerOS.Api.Infrastructure;

public sealed class MinioInspectionPhotoStorage(IMinioClient client, IConfiguration configuration,
    ILogger<MinioInspectionPhotoStorage> logger) : IInspectionPhotoStorage
{
    private readonly string _bucket = configuration["ObjectStorage:Bucket"] ?? "dealeros-private";

    public async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), cancellationToken))
                await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), cancellationToken);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            throw new StorageUnavailableException("Не удалось подготовить bucket фотографий.", exception);
        }
    }

    public async Task PutAsync(string objectKey, Stream content, long sizeBytes, string contentType,
        CancellationToken cancellationToken)
    {
        try
        {
            var args = new PutObjectArgs().WithBucket(_bucket).WithObject(objectKey).WithStreamData(content)
                .WithObjectSize(sizeBytes).WithContentType(contentType);
            await client.PutObjectAsync(args, cancellationToken);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            throw new StorageUnavailableException("Не удалось загрузить фотографию.", exception);
        }
    }

    public async Task<Stream> GetAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            var output = new MemoryStream();
            var args = new GetObjectArgs().WithBucket(_bucket).WithObject(objectKey)
                .WithCallbackStream(async (stream, ct) => await stream.CopyToAsync(output, ct));
            await client.GetObjectAsync(args, cancellationToken);
            output.Position = 0;
            return output;
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            throw new StorageUnavailableException("Не удалось скачать фотографию.", exception);
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(_bucket).WithObject(objectKey),
                cancellationToken);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            logger.LogWarning(exception, "Failed to remove inspection object {ObjectKey}", objectKey);
            throw new StorageUnavailableException("Не удалось удалить фотографию.", exception);
        }
    }

    private static bool IsStorageException(Exception exception) => exception is MinioException or HttpRequestException
        or IOException or TaskCanceledException;
}
