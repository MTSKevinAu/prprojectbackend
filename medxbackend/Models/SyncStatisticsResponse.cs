namespace medxbackend.Models;

/// <summary>Counts all synchronization queue rows and the exact pending, succeeded and failed states.</summary>
public sealed record SyncStatisticsResponse(
    long TotalRecords,
    long PendingRecords,
    long SucceededRecords,
    long FailedRecords);
