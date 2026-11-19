using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

/// <summary>Selection criteria for preparing and previewing LIS synchronization data.</summary>
public sealed record LisPreviewOrdersRequest
{
    public DateOnly? SequenceDate { get; init; }

    [Required, FormTypes]
    public string[] FormTypes { get; init; } = ["DR"];

    [Required, StringLength(20)]
    public string Status { get; init; } = "Matched";
}
