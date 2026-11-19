using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace medxbackend.Models;

/// <summary>Selects a preparation date range and final patients to synchronize in one shared batch.</summary>
public sealed record SyncBatchRequest
{
    [Required]
    [JsonPropertyName("start_date")]
    public DateOnly? StartDate { get; init; }

    [Required]
    [JsonPropertyName("end_date")]
    public DateOnly? EndDate { get; init; }

    [Required, MinLength(1)]
    public string[] CentreIds { get; init; } = [];
}

/// <summary>Reports per-system batch outcomes and per-order delivery results.</summary>
public sealed record SyncBatchResponse(bool Success, IReadOnlyList<SyncBatchTargetResponse> Results);

/// <summary>Reports one system's outcomes with the shared batch identifier, including orders not attempted.</summary>
public sealed record SyncBatchTargetResponse(
    string TargetSystem,
    long? BatchId,
    bool Success,
    string? Stage,
    string? Message,
    IReadOnlyList<SyncBatchOrderResponse> Orders);

/// <summary>Associates a delivery result with its selected local patient.</summary>
public sealed record SyncBatchOrderResponse(string CentreId, string FormType, SyncPatientTargetResult Result);
