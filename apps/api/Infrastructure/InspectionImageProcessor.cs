using DealerOS.Modules.Inspections.Application;
using DealerOS.SharedKernel;
using SkiaSharp;

namespace DealerOS.Api.Infrastructure;

public sealed class InspectionImageProcessor : IInspectionImageProcessor
{
    public const long MaxInputBytes = 8 * 1024 * 1024;
    public const long MaxPixels = 25_000_000;
    public const int ThumbnailMaxEdge = 480;
    public const int MediumMaxEdge = 1280;
    public const int LargeMaxEdge = 2560;

    public async Task<NormalizedInspectionImage> NormalizeAsync(Stream input, long declaredLength,
        string originalFileName, string? declaredContentType, CancellationToken cancellationToken)
    {
        using var decoded = await DecodeAsync(input, declaredLength, cancellationToken);
        var encoded = Encode(decoded.Bitmap, decoded.Format, decoded.ContentType, decoded.Extension, 88);
        EnsureOutputSize(encoded);
        return new NormalizedInspectionImage(new MemoryStream(encoded.Content, writable: false), encoded.ContentType,
            encoded.Extension, encoded.SizeBytes, encoded.Width, encoded.Height);
    }

    public async Task<ProcessedVehicleImageSet> CreateVehicleGallerySetAsync(Stream input, long declaredLength,
        string originalFileName, string? declaredContentType, CancellationToken cancellationToken)
    {
        using var decoded = await DecodeAsync(input, declaredLength, cancellationToken);
        var original = Encode(decoded.Bitmap, decoded.Format, decoded.ContentType, decoded.Extension, 90);
        var thumbnail = EncodeResized(decoded.Bitmap, ThumbnailMaxEdge, decoded.Format, decoded.ContentType,
            decoded.Extension, 82);
        var medium = EncodeResized(decoded.Bitmap, MediumMaxEdge, decoded.Format, decoded.ContentType,
            decoded.Extension, 85);
        var large = EncodeResized(decoded.Bitmap, LargeMaxEdge, decoded.Format, decoded.ContentType,
            decoded.Extension, 88);
        EnsureOutputSize(original);
        return new ProcessedVehicleImageSet(original, thumbnail, medium, large);
    }

    private static async Task<DecodedImage> DecodeAsync(Stream input, long declaredLength,
        CancellationToken cancellationToken)
    {
        if (declaredLength <= 0)
            throw new DomainException("inspection_photo.empty", "Файл фотографии пуст.");
        if (declaredLength > MaxInputBytes)
            throw new DomainException("inspection_photo.too_large", "Фотография не должна превышать 8 МиБ.");

        await using var buffer = new MemoryStream((int)declaredLength);
        await input.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length != declaredLength || buffer.Length > MaxInputBytes)
            throw new DomainException("inspection_photo.invalid_length",
                "Размер загруженного файла не совпадает с заявленным.");
        buffer.Position = 0;

        using var codec = SKCodec.Create(buffer)
            ?? throw new DomainException("inspection_photo.invalid_type",
                "Содержимое файла не является поддерживаемым изображением.");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0
            || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
            throw new DomainException("inspection_photo.too_many_pixels",
                "Разрешение фотографии превышает 25 мегапикселей.");

        var (format, contentType, extension) = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => (SKEncodedImageFormat.Jpeg, "image/jpeg", "jpg"),
            SKEncodedImageFormat.Png => (SKEncodedImageFormat.Png, "image/png", "png"),
            SKEncodedImageFormat.Webp => (SKEncodedImageFormat.Webp, "image/webp", "webp"),
            _ => throw new DomainException("inspection_photo.unsupported_type",
                "Поддерживаются JPEG, PNG и WebP. HEIC/HEIF пока не поддерживается.")
        };

        using var decoded = new SKBitmap(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888,
            SKAlphaType.Premul);
        var result = codec.GetPixels(decoded.Info, decoded.GetPixels());
        if (result == SKCodecResult.IncompleteInput)
            throw new DomainException("inspection_photo.invalid_content", "Файл изображения загружен не полностью.");
        if (result != SKCodecResult.Success)
            throw new DomainException("inspection_photo.invalid_content", "Файл изображения повреждён.");

        var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);
        return new DecodedImage(oriented, format, contentType, extension);
    }

    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        var swapsDimensions = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var output = new SKBitmap(swapsDimensions ? source.Height : source.Width,
            swapsDimensions ? source.Width : source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Transparent);
        var matrix = SKMatrix.CreateIdentity();
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                matrix.ScaleX = -1; matrix.TransX = source.Width;
                break;
            case SKEncodedOrigin.BottomRight:
                matrix.ScaleX = -1; matrix.ScaleY = -1;
                matrix.TransX = source.Width; matrix.TransY = source.Height;
                break;
            case SKEncodedOrigin.BottomLeft:
                matrix.ScaleY = -1; matrix.TransY = source.Height;
                break;
            case SKEncodedOrigin.LeftTop:
                matrix.ScaleX = 0; matrix.SkewX = 1;
                matrix.SkewY = 1; matrix.ScaleY = 0;
                break;
            case SKEncodedOrigin.RightTop:
                matrix.ScaleX = 0; matrix.SkewX = -1; matrix.TransX = source.Height;
                matrix.SkewY = 1; matrix.ScaleY = 0;
                break;
            case SKEncodedOrigin.RightBottom:
                matrix.ScaleX = 0; matrix.SkewX = -1; matrix.TransX = source.Height;
                matrix.SkewY = -1; matrix.ScaleY = 0; matrix.TransY = source.Width;
                break;
            case SKEncodedOrigin.LeftBottom:
                matrix.ScaleX = 0; matrix.SkewX = 1;
                matrix.SkewY = -1; matrix.ScaleY = 0; matrix.TransY = source.Width;
                break;
        }
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        canvas.Flush();
        return output;
    }

    private static ProcessedImageVariant EncodeResized(SKBitmap source, int maxEdge, SKEncodedImageFormat format,
        string contentType, string extension, int quality)
    {
        var scale = Math.Min(1d, maxEdge / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        if (width == source.Width && height == source.Height)
            return Encode(source, format, contentType, extension, quality);

        using var resized = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(resized);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, new SKRect(0, 0, width, height),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Flush();
        return Encode(resized, format, contentType, extension, quality);
    }

    private static ProcessedImageVariant Encode(SKBitmap bitmap, SKEncodedImageFormat format, string contentType,
        string extension, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, format == SKEncodedImageFormat.Png ? 100 : quality)
            ?? throw new DomainException("inspection_photo.encode_failed",
                "Не удалось безопасно обработать изображение.");
        return new ProcessedImageVariant(data.ToArray(), contentType, extension, bitmap.Width, bitmap.Height);
    }

    private static void EnsureOutputSize(ProcessedImageVariant image)
    {
        if (image.SizeBytes > MaxInputBytes)
            throw new DomainException("inspection_photo.too_large",
                "Обработанная фотография превышает лимит 8 МиБ.");
    }

    private sealed class DecodedImage(SKBitmap bitmap, SKEncodedImageFormat format, string contentType,
        string extension) : IDisposable
    {
        public SKBitmap Bitmap { get; } = bitmap;
        public SKEncodedImageFormat Format { get; } = format;
        public string ContentType { get; } = contentType;
        public string Extension { get; } = extension;
        public void Dispose() => Bitmap.Dispose();
    }
}
