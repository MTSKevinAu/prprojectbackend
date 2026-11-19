namespace medxbackend.Services;

public sealed class BarcodeOptions
{
    public const string SectionName = "Barcode";

    public string Format { get; set; } = "Code39";
}