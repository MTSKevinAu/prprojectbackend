namespace medxbackend.Services;

public interface IReceiptPrinterService
{
    Task<int> PrintPdfAsync(byte[] pdfBytes);
}