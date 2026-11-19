using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

/// <summary>Criteria for generating centreids and preparing the CRM synchronization queue.</summary>
public sealed record CrmPreviewRequest
{
    public DateOnly? SequenceDate { get; init; }

    [Required, FormTypes]
    public string[] FormTypes { get; init; } = ["DR"];

    [Required, StringLength(20)]
    public string Status { get; init; } = "Matched";
}
