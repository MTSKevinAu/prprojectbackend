namespace medxbackend.Models;

/// <summary>Batch upsert results; inspect Success and each item even when HTTP status is 200.</summary>
public sealed record CrmSyncPatientDetailsResponse(
    bool Success,
    int Total,
    int Created,
    int Updated,
    int Failed,
    int Unknown,
    int NotAttempted,
    IReadOnlyList<CrmSyncPatientDetailsItemResponse> Items);

/// <summary>One export row's upsert result, with a one-based row number and safe error details.</summary>
public sealed record CrmSyncPatientDetailsItemResponse(
    int RowNumber,
    string? LabNo,
    string? DailyNo,
    string Outcome,
    string? SalesforceId = null,
    int? HttpStatus = null,
    string? ErrorCode = null,
    string? Message = null);
