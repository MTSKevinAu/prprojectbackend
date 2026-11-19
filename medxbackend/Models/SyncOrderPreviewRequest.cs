using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace medxbackend.Models;

/// <summary>Selects an inclusive date range for preparing and previewing synchronization queue records.</summary>
public sealed record SyncOrderPreviewRequest
{
    [Required]
    [JsonPropertyName("start_date")]
    public DateOnly? StartDate { get; init; }

    [Required]
    [JsonPropertyName("end_date")]
    public DateOnly? EndDate { get; init; }
}

/// <summary>Reports orders prepared for enabled rules without submitting them to external systems.</summary>
public sealed record SyncOrderPreviewResponse(
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalCount,
    IReadOnlyList<SyncOrderPreviewItemResponse> Results);

/// <summary>Reports one order and target system, including its existing synchronization state.</summary>
public sealed record SyncOrderPreviewItemResponse(
    long Id,
    string TargetSystem,
    string CentreId,
    string FormType,
    string DailyNo,
    string SyncStatus,
    int AttemptCount,
    string? RemoteRecordId,
    int? LastHttpStatus,
    string? LastError);
