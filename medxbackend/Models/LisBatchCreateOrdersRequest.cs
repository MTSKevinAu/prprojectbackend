using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

/// <summary>Patient selection criteria for sequential LIS order creation using server defaults.</summary>
public sealed record LisBatchCreateOrdersRequest
{
    public DateOnly? SequenceDate { get; init; }

    [Required, FormTypes]
    public string[] FormTypes { get; init; } = ["DR"];

    [Required, StringLength(20)]
    public string Status { get; init; } = "Matched";
}
