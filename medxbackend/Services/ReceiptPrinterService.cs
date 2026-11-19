using Microsoft.Extensions.Options;
using PDFtoImage;
using SkiaSharp;
using System.Net.Sockets;
using System.Text;

namespace medxbackend.Services;

public sealed class ReceiptPrinterService : IReceiptPrinterService
{
    private readonly NetworkPrinterOptions _options;

    public ReceiptPrinterService(IOptions<NetworkPrinterOptions> options)
    {
        _options = options.Value;
    }

    public async Task<int> PrintPdfAsync(byte[] pdfBytes)
    {
        if (string.IsNullOrWhiteSpace(_options.Host)
            || _options.Host.Contains(':')
            || _options.Port is < 1 or > 65535
            || _options.CopiesPerLabel is < 1 or > 9999
            || _options.PrintWidthDots is < 8 or > 2048
            || _options.LabelWidthMillimeters <= 0
            || _options.LabelHeightMillimeters <= 0
            || _options.GapMillimeters < 0)
        {
            throw new PrinterNotConfiguredException();
        }

        using var output = new MemoryStream();
        var pagesPrinted = 0;
        var renderOptions = new RenderOptions(
            Width: _options.PrintWidthDots,
            WithAspectRatio: true);

        foreach (var bitmap in RenderPages())
        {
            using (bitmap)
            {
                WriteLabel(bitmap, output);
            }

            pagesPrinted++;
        }

        if (pagesPrinted == 0)
        {
            throw new InvalidDataException("The generated PDF contains no pages.");
        }

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Host, _options.Port).WaitAsync(TimeSpan.FromSeconds(5));
            await using var stream = client.GetStream();
            await stream.WriteAsync(output.ToArray());
            await stream.FlushAsync();
        }
        catch (TimeoutException exception)
        {
            throw new IOException("Timed out connecting to the TSC network printer.", exception);
        }

        return pagesPrinted;

        void WriteLabel(SKBitmap bitmap, Stream destination)
        {
            var bytesPerRow = (bitmap.Width + 7) / 8;
            var pixels = new byte[bytesPerRow * bitmap.Height];
            Array.Fill(pixels, byte.MaxValue);

            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    var luminance = 0.299 * color.Red + 0.587 * color.Green + 0.114 * color.Blue;
                    if (luminance < 128)
                    {
                        pixels[y * bytesPerRow + x / 8] &= (byte)~(0x80 >> (x % 8));
                    }
                }
            }

            var header = Encoding.ASCII.GetBytes(
                $"SIZE {_options.LabelWidthMillimeters} mm,{_options.LabelHeightMillimeters} mm\r\n"
                + $"GAP {_options.GapMillimeters} mm,0 mm\r\n"
                + "DIRECTION 0\r\nCLS\r\n"
                + $"BITMAP 0,0,{bytesPerRow},{bitmap.Height},0,");
            destination.Write(header);
            destination.Write(pixels);
            destination.Write(Encoding.ASCII.GetBytes($"\r\nPRINT {_options.CopiesPerLabel},1\r\n"));
        }

        IEnumerable<SKBitmap> RenderPages()
        {
            if (OperatingSystem.IsWindows())
            {
                return Conversion.ToImages(pdfBytes, options: renderOptions);
            }

            if (OperatingSystem.IsLinux())
            {
                return Conversion.ToImages(pdfBytes, options: renderOptions);
            }

            if (OperatingSystem.IsMacOS())
            {
                return Conversion.ToImages(pdfBytes, options: renderOptions);
            }

            throw new PlatformNotSupportedException("PDF printing is supported on Windows, Linux, and macOS.");
        }
    }
}