using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

/// <summary>Identifies a local patient synchronization record to create or retry an LIS order.</summary>
public sealed record LisCreateOrderFromPatientRequest
{
    [Required, StringLength(255)]
    public string CentreId { get; init; } = string.Empty;

    [Required]
    public string TargetSystem { get; init; } = "LIS";
}
