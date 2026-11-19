using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace medxbackend.Models;

/// <summary>Selects final patient records by centreid and their CRM synchronization queue.</summary>
public sealed record CrmSyncPatientDetailsRequest
{
    [Required]
    [JsonPropertyName("centreid")]
    public required string CentreId { get; init; }

    [Required]
    [JsonPropertyName("targetSystem")]
    public required string TargetSystem { get; init; }
}
