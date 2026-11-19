using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

/// <summary>Selects one final patient for synchronization using enabled form-type rules.</summary>
public sealed record SyncPatientRequest
{
    [Required, StringLength(255)]
    public string CentreId { get; init; } = string.Empty;
}

/// <summary>Reports delivery results for every enabled target system.</summary>
public sealed record SyncPatientResponse(
    string CentreId,
    string FormType,
    bool Success,
    IReadOnlyList<SyncPatientTargetResult> Results);

/// <summary>Reports one system's result without exposing credentials or patient payloads.</summary>
public sealed record SyncPatientTargetResult(
    string TargetSystem,
    bool Success,
    string Operation,
    int HttpStatus,
    string? Stage = null,
    string? Message = null);
