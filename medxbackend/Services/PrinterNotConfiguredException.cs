namespace medxbackend.Services;

public sealed class PrinterNotConfiguredException : Exception
{
    public PrinterNotConfiguredException()
        : base("The network printer is not configured with a valid host, port, and print width.")
    {
    }
}