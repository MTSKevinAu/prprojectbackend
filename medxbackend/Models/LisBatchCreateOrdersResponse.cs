namespace medxbackend.Models;

/// <summary>Batch counts and processed results; processing stops at the first failure.</summary>
public sealed record LisBatchCreateOrdersResponse(
    int TotalCount,
    int SkippedCount,
    int SucceededCount,
    int FailedCount,
    int UnprocessedCount,
    IReadOnlyList<LisBatchOrderResult> Results);

/// <summary>A processed patient order with its HTTP status and original response or local error.</summary>
public sealed record LisBatchOrderResult(
    string DailyNo,
    bool Success,
    int StatusCode,
    string? LabId,
    string? ContentType,
    object? Response,
    long? SyncId = null,
    string? CentreId = null);
