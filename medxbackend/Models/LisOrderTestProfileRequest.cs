using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

/// <summary>A test profile requested for an LIS order.</summary>
public sealed record LisOrderTestProfileRequest
{
    [Required]
    public string TestProfileCode { get; init; } = string.Empty;

    [Required]
    public bool? IsBillable { get; init; } = true;
}