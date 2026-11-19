using medxbackend.Models;

namespace medxbackend.Services;

public interface ISyncRoutingRepository
{
    Task<RecentSyncResponse> LoadRecent(int? count, string? targetSystem, CancellationToken cancellationToken);
    Task<SyncStatisticsResponse> GetStatistics(CancellationToken cancellationToken);
    Task<List<SyncRoutingRule>> LoadEnabledRules(CancellationToken cancellationToken);
    Task PrepareOrders(string target, DateOnly sequenceDate, string[] formTypes, CancellationToken cancellationToken);
    Task<List<SyncOrderPreviewItemResponse>> LoadPreview(
        DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken);
    Task<List<SyncRoutingPatient>> LoadPatients(string centreId, CancellationToken cancellationToken);
    Task<List<string>> LoadTargets(string formType, CancellationToken cancellationToken);
    Task<SyncRoutingQueue> GetOrCreateQueue(SyncRoutingPatient patient, string target, CancellationToken cancellationToken);
    Task<SyncRoutingQueue?> LoadQueue(SyncRoutingPatient patient, string target, CancellationToken cancellationToken);
    Task<bool> SaveLisTokenFailure(long syncId, int? httpStatus, string error, CancellationToken cancellationToken);
}

public sealed record SyncRoutingPatient(string CentreId, string FormType, string DailyNo);
public sealed record SyncRoutingQueue(long Id, string Status, string? RemoteRecordId);
public sealed record SyncRoutingRule(string FormType, string TargetSystem);
