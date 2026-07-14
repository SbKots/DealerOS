using DealerOS.Modules.Inspections.Application;
using DealerOS.SharedKernel;
using SkiaSharp;

namespace DealerOS.Api.Infrastructure;

public sealed class InspectionImageProcessor : IInspectionImageProcessor
{
    public const long MaxInputBytes = 8 * 1024 * 1024;
    public const long MaxPixels = 25_000_000;

    public async Task<NormalizedInspectionImage> NormalizeAsync(Stream input, long declaredLength,
        string originalFileName, string? declaredContentType, CancellationToken cancellationToken)
    {
        if (declaredLength <= 0) throw new DomainException("inspection_photo.empty", "Файл фотографии пуст.");
        if (declaredLength > MaxInputBytes)
            throw new DomainException("inspection_photo.too_large", "Фотография не должна превышать 8 MiB.");

        await using var buffer = new MemoryStream((int)declaredLength);
        await input.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length != declaredLength || buffer.Length > MaxInputBytes)
            throw new DomainException("inspection_photo.invalid_length", "Размер загруженного файла не совпадает с заявленным.");
        buffer.Position = 0;

        using var codec = SKCodec.Create(buffer)
            ?? throw new DomainException("inspection_photo.invalid_type", "Содержимое файла не является изображением.");
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
            throw new DomainException("inspection_photo.too_many_pixels", "Разрешение фотографии превышает 25 мегапикселей.");

        var (encodedFormat, contentType, extension) = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => (SKEncodedImageFormat.Jpeg, "image/jpeg", "jpg"),
            SKEncodedImageFormat.Png => (SKEncodedImageFormat.Png, "image/png", "png"),
            SKEncodedImageFormat.Webp => (SKEncodedImageFormat.Webp, "image/webp", "webp"),
            _ => throw new DomainException("inspection_photo.unsupported_type", "Разрешены только JPEG, PNG и WebP.")
        };

        using var bitmap = new SKBitmap(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var decodeResult = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (decodeResult is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new DomainException("inspection_photo.invalid_content", "Файл изображения повреждён.");
        if (decodeResult == SKCodecResult.IncompleteInput)
            throw new DomainException("inspection_photo.invalid_content", "Файл изображения загружен не полностью.");

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(encodedFormat, encodedFormat == SKEncodedImageFormat.Png ? 100 : 88)
            ?? throw new DomainException("inspection_photo.encode_failed", "Не удалось нормализовать изображение.");
        if (data.Size > MaxInputBytes)
            throw new DomainException("inspection_photo.too_large", "Нормализованная фотография превышает 8 MiB.");
        var output = new MemoryStream(data.ToArray(), writable: false);
        return new NormalizedInspectionImage(output, contentType, extension, output.Length);
    }
}
