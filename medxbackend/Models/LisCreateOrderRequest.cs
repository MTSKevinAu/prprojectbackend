using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace medxbackend.Models;

/// <summary>An LIS order associated with a local patient record.</summary>
public sealed record LisCreateOrderRequest : LisOrderParameters
{
    public LisCreateOrderRequest()
    {
    }

    public LisCreateOrderRequest(LisOrderParameters parameters) : base(parameters)
    {
    }

    [Required]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public string? DailyNo { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)]
    public string? LabId { get; init; }

    [Required]
    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; init; } = string.Empty;

    [Required]
    [JsonPropertyName("patient")]
    public LisOrderPatientRequest? Patient { get; init; }
}
