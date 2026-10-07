namespace medxbackend.Models;

public sealed class GetPatientDetailsByDailynoRequest
{
    public string? DailyNo { get; set; }
    public string? FormType { get; set; }
    public int? InputType { get; set; }
    public int? UserId { get; set; }
}
