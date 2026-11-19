using System.Text.Json;
using medxbackend.Models;

namespace medxbackend.Services;

public interface ISyncHistoryService
{
    bool IsRecording { get; }
    bool HasAttempt { get; }
    long SyncId { get; }
    long? BatchId { get; }
    Task<SyncPatientTargetResult> RunBatch(
        string target, Func<Task<SyncPatientTargetResult>> operation, CancellationToken cancellationToken);
    Task<SyncPatientTargetResult> RunAttempt(
        long syncId, string target, Func<Task<SyncPatientTargetResult>> operation);
    Task<SyncPatientTargetResult> Run(
        string target, long syncId, Func<Task<SyncPatientTargetResult>> operation, CancellationToken cancellationToken);
    Task<bool> Claim(
        long syncId, JsonElement payload, string[] allowedStates, bool confirmRemote,
        CancellationToken cancellationToken);
    Task<bool> Complete(
        long syncId, string status, string? remoteId, int? httpStatus, string? error,
        CancellationToken cancellationToken);
}
