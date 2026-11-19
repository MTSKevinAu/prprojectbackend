namespace medxbackend.Models;

/// <summary>All synchronization records after preparing the selected centreids.</summary>
public sealed record LisBatchPreviewResponse(
    int TotalCount,
    IReadOnlyList<LisSyncRecordResponse> Results);

/// <summary>All columns of a patientdetails_sync record.</summary>
public sealed record LisSyncRecordResponse(
    long Id,
    string TargetSystem,
    string CentreId,
    string FormType,
    DateOnly? SequenceDate,
    string SyncStatus,
    int AttemptCount,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? SyncedAt,
    string? RemoteRecordId,
    int? LastHttpStatus,
    string? LastError,
    DateTimeOffset CreatedAt);
