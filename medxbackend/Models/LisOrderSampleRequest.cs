using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

/// <summary>A specimen and its collection details for an LIS order.</summary>
public sealed record LisOrderSampleRequest
{
    [Required]
    public string SampleCode { get; init; } = string.Empty;

    [Required, RegularExpression("^(P|A|L)$")]
    public string Status { get; init; } = string.Empty;

    [Required, Range(0, int.MaxValue)]
    public int? LabelCount { get; init; }

    public DateTimeOffset? CollectionDatetime { get; init; }
    public DateTimeOffset? ReceivedDatetime { get; init; }
    public string? SpecimenComment { get; init; }
}