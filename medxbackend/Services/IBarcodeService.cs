namespace medxbackend.Services;

public interface IBarcodeService
{
    byte[] GenerateCode128Png(string value, int width = 300, int height = 100);

    byte[] GenerateCode39Png(string value, int width = 300, int height = 100);
}