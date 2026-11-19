namespace medxbackend.Models;

/// <summary>CRM synchronization queue records after generating the selected centreids.</summary>
public sealed record CrmBatchPreviewResponse(
    int TotalCount,
    IReadOnlyList<CrmSyncRecordResponse> Results);

/// <summary>All columns of a CRM patientdetails_sync record.</summary>
public sealed record CrmSyncRecordResponse(
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

/// <summary>Locally prepared upsert requests used by the synchronization service.</summary>
public sealed record CrmUpsertPreviewResponse(
    int TotalCount,
    int ValidCount,
    int InvalidCount,
    IReadOnlyList<CrmPatientDetailsPreviewResponse> Results);

/// <summary>A locally validated Salesforce upsert request; invalid rows have no request body or path.</summary>
public sealed record CrmPatientDetailsPreviewResponse(
    int RowNumber,
    string? LabNo,
    string? DailyNo,
    bool IsValid,
    string Method,
    string? Path,
    IReadOnlyDictionary<string, object?>? Payload,
    string? ErrorCode,
    string? Message);
