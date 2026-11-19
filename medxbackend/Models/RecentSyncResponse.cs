namespace medxbackend.Models;

/// <summary>Returns history from the globally latest completed batch, including an empty matching batch.</summary>
public sealed record RecentSyncResponse(
    int TotalCount,
    IReadOnlyList<RecentSyncRecordResponse> Results,
    IReadOnlyList<RecentSyncBatchResponse> Batches);

/// <summary>Reports a historical attempt with its current queue identifier, attempt count and patient daily number.</summary>
public sealed record RecentSyncRecordResponse(
    long Id,
    string TargetSystem,
    string CentreId,
    string FormType,
    string SyncStatus,
    int AttemptCount,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? SyncedAt,
    string? RemoteRecordId,
    int? LastHttpStatus,
    string? LastError,
    string? DailyNo,
    long HistoryId,
    long BatchId,
    DateTimeOffset? CompletedAt);

/// <summary>Identifies the globally latest completed batch shared across target systems.</summary>
public sealed record RecentSyncBatchResponse(
    long Id,
    string BatchStatus,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
