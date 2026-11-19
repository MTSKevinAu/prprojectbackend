namespace medxbackend.Services;

public sealed class NetworkPrinterOptions
{
    public const string SectionName = "NetworkPrinter";

    public string Host { get; set; } = string.Empty;

    public string PrinterName { get; set; } = "MedX Network Printer";

    public int Port { get; set; } = 9100;

    public int CopiesPerLabel { get; set; } = 1;

    public int PrintWidthDots { get; set; } = 440;

    public double LabelWidthMillimeters { get; set; } = 55;

    public double LabelHeightMillimeters { get; set; } = 25;

    public double GapMillimeters { get; set; } = 2;
}