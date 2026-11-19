namespace medxbackend.Models;

public sealed class GenerateCode128Request
{
    public string? Value { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}