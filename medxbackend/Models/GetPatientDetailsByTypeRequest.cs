namespace medxbackend.Models;

public sealed class GetPatientDetailsByTypeRequest
{
    public int? InputType { get; set; }
    public string? FormType { get; set; }
}