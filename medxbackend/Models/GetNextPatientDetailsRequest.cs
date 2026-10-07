namespace medxbackend.Models;

public sealed class GetNextPatientDetailsRequest
{
    public string? FormType { get; set; }
    public int? InputType { get; set; }
    public DateTime? DateReceived { get; set; }
    public string? DailyNo { get; set; }
}