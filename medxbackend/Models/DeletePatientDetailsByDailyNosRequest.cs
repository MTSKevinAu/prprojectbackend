namespace medxbackend.Models;

public sealed class DeletePatientDetailsByDailyNosRequest
{
    public DeletePatientDetailsByDailyNoItem[]? Items { get; set; }
}

public sealed class DeletePatientDetailsByDailyNoItem
{
    public string? DailyNo { get; set; }
    public int? InputType { get; set; }
}