using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.SkiaSharp;

namespace medxbackend.Services;

public sealed class BarcodeService : IBarcodeService
{
    public byte[] GenerateCode128Png(string value, int width = 300, int height = 100)
    {
        return GeneratePng(value, BarcodeFormat.CODE_128, width, height);
    }

    public byte[] GenerateCode39Png(string value, int width = 300, int height = 100)
    {
        return GeneratePng(value, BarcodeFormat.CODE_39, width, height);
    }

    private static byte[] GeneratePng(string value, BarcodeFormat format, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var writer = new BarcodeWriter
        {
            Format = format,
            Options = new EncodingOptions
            {
                Width = width,
                Height = height,
                Margin = 10,
                PureBarcode = true
            }
        };

        using var bitmap = writer.Write(value);
        using var image = SKImage.FromBitmap(bitmap);
        using var encodedImage = image.Encode(SKEncodedImageFormat.Png, 100);
        return encodedImage.ToArray();
    }
}