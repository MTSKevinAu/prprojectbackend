using System.Text.Json.Serialization;

namespace medxbackend.Models;

public sealed class GetPatientDetailsDailyNoGapsRequest
{
    [JsonPropertyName("formtypes")]
    public List<string?>? FormTypes { get; set; }

    [JsonPropertyName("sequence_date")]
    public DateOnly? SequenceDate { get; set; }
}