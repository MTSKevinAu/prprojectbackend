using System.Text.Json;
using medxbackend.Data;
using medxbackend.Models;
using Npgsql;
using NpgsqlTypes;

namespace medxbackend.Services;

public sealed class SyncHistoryService(DbHelper dbHelper, ILogger<SyncHistoryService> logger) : ISyncHistoryService
{
    private long? batchId;
    private long? historyId;
    private string? target;
    private bool completed;
    public bool IsRecording => batchId.HasValue;
    public bool HasAttempt => historyId.HasValue;
    public long SyncId { get; private set; }
    public long? BatchId => batchId;

    public Task<SyncPatientTargetResult> RunBatch(
        string target, Func<Task<SyncPatientTargetResult>> operation, CancellationToken cancellationToken) =>
        Run(target, 0, operation, cancellationToken);

    public async Task<SyncPatientTargetResult> RunAttempt(
        long syncId, string target, Func<Task<SyncPatientTargetResult>> operation)
    {
        if (!IsRecording || HasAttempt || SyncId != 0)
            throw new InvalidOperationException("A batch without an active attempt is required.");
        SyncId = syncId;
        this.target = target;
        try
        {
            return await operation();
        }
        finally
        {
            if (!await FinishAttempt())
                throw new NpgsqlException("The active synchronization history could not be completed.");
            historyId = null;
            completed = false;
            SyncId = 0;
        }
    }

    public async Task<SyncPatientTargetResult> Run(
        string target, long syncId, Func<Task<SyncPatientTargetResult>> operation, CancellationToken cancellationToken)
    {
        if (IsRecording)
            throw new InvalidOperationException("Synchronization batches cannot be nested.");
        var inserted = await dbHelper.QueryAsync(
            """
            INSERT INTO public.patientdetails_sync_batches
            DEFAULT VALUES RETURNING id
            """,
            reader => reader.GetInt64(0), cancellationToken: cancellationToken);
        batchId = inserted.Single();
        this.target = target;
        SyncId = syncId;
        SyncPatientTargetResult? result = null;
        var saved = false;
        try
        {
            result = await operation();
        }
        finally
        {
            try
            {
                saved = await Finish(result);
            }
            finally
            {
                batchId = null;
                historyId = null;
                this.target = null;
                completed = false;
                SyncId = 0;
            }
        }
        return saved ? result! : new(target, false, "Sync", 500, "SaveSyncHistory",
            "The synchronization batch could not be completed. Verify its history and remote outcome before retrying.");
    }

    public async Task<bool> Claim(
        long syncId, JsonElement payload, string[] allowedStates, bool confirmRemote,
        CancellationToken cancellationToken)
    {
        if (!IsRecording || HasAttempt || syncId != SyncId || payload.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("An active batch and one exact business payload are required.");
        var rows = await dbHelper.QueryAsync(
            """
            WITH candidate AS (
                SELECT id FROM public.patientdetails_sync
                WHERE id = @syncId AND target_system = @target AND sync_status = ANY(@allowedStates)
                FOR UPDATE
            ), claimed AS (
                UPDATE public.patientdetails_sync AS q
                SET sync_status = 'in_progress', attempt_count = attempt_count + 1,
                    remote_record_id = CASE WHEN @confirmRemote THEN q.centreid
                        WHEN q.sync_status = 'succeeded' THEN COALESCE(q.remote_record_id, q.centreid)
                        ELSE q.remote_record_id END,
                    last_attempt_at = clock_timestamp(), last_http_status = NULL, last_error = NULL
                FROM candidate AS c
                WHERE q.id = c.id AND q.sync_status = ANY(@allowedStates)
                  AND (NOT @confirmRemote OR q.sync_status = 'succeeded'
                       OR (q.sync_status = 'unknown' AND q.remote_record_id = q.centreid))
                  AND NOT EXISTS (
                      SELECT 1 FROM public.patientdetails_sync_history AS h
                      WHERE h.sync_id = q.id AND h.sync_status IN ('pending', 'in_progress'))
                RETURNING q.id
            )
            INSERT INTO public.patientdetails_sync_history
                (batch_id, sync_id, target_system, payload, sync_status, created_at, attempted_at)
            SELECT @batchId, id, @target, @payload, 'in_progress', statement_timestamp(), clock_timestamp()
            FROM claimed
            RETURNING id
            """,
            reader => reader.GetInt64(0),
            [
                new NpgsqlParameter("syncId", syncId),
                new NpgsqlParameter("target", target!),
                new NpgsqlParameter("batchId", batchId!.Value),
                new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload.GetRawText() },
                new NpgsqlParameter("allowedStates", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = allowedStates },
                new NpgsqlParameter("confirmRemote", confirmRemote)
            ], cancellationToken);
        if (rows.Count == 0)
            return false;
        historyId = rows.Single();
        SyncId = syncId;
        return true;
    }

    public async Task<bool> Complete(
        long syncId, string status, string? remoteId, int? httpStatus, string? error,
        CancellationToken cancellationToken)
    {
        if (!HasAttempt || completed || syncId != SyncId
            || status is not ("succeeded" or "failed" or "unknown"))
            throw new InvalidOperationException("Only the active synchronization attempt can be completed.");
        var affected = await dbHelper.ExecuteNonQueryAsync(
            """
            WITH saved AS (
                UPDATE public.patientdetails_sync AS q
                SET sync_status = @status,
                    synced_at = CASE WHEN @status = 'succeeded' THEN clock_timestamp() ELSE NULL END,
                    remote_record_id = COALESCE(@remoteId, q.remote_record_id),
                    last_http_status = @httpStatus, last_error = @error
                WHERE q.id = @syncId AND q.target_system = @target AND q.sync_status = 'in_progress'
                  AND EXISTS (
                      SELECT 1 FROM public.patientdetails_sync_history AS h
                      WHERE h.id = @historyId AND h.batch_id = @batchId
                        AND h.sync_id = q.id AND h.sync_status = 'in_progress')
                RETURNING q.id, q.synced_at, q.remote_record_id
            )
            UPDATE public.patientdetails_sync_history AS h
            SET sync_status = @status, completed_at = clock_timestamp(),
                synced_at = saved.synced_at, remote_record_id = saved.remote_record_id,
                last_http_status = @httpStatus, last_error = @error
            FROM saved WHERE h.id = @historyId AND h.sync_id = saved.id AND h.sync_status = 'in_progress'
            """,
            [
                new NpgsqlParameter("syncId", syncId), new NpgsqlParameter("target", target!),
                new NpgsqlParameter("historyId", historyId!.Value), new NpgsqlParameter("batchId", batchId!.Value),
                new NpgsqlParameter("status", status),
                new NpgsqlParameter("remoteId", NpgsqlDbType.Text) { Value = (object?)remoteId ?? DBNull.Value },
                new NpgsqlParameter("httpStatus", NpgsqlDbType.Integer) { Value = (object?)httpStatus ?? DBNull.Value },
                new NpgsqlParameter("error", NpgsqlDbType.Text) { Value = (object?)error ?? DBNull.Value }
            ], cancellationToken);
        completed = affected == 1;
        if (!completed)
            logger.LogError("Unable to complete synchronization history {HistoryId} for queue {SyncId}.", historyId, syncId);
        return completed;
    }

    private async Task<bool> FinishAttempt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            return !HasAttempt || completed || await Complete(SyncId, "unknown", null, null,
                "The synchronization attempt did not finish normally. Verify its remote outcome before retrying.",
                timeout.Token);
        }
        catch (Exception exception) when (exception is NpgsqlException or OperationCanceledException)
        {
            logger.LogError(exception, "Unable to finish synchronization history {HistoryId}.", historyId);
            return false;
        }
    }

    private async Task<bool> Finish(SyncPatientTargetResult? result)
    {
        if (!await FinishAttempt())
            return false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var affected = await dbHelper.ExecuteNonQueryAsync(
                """
                WITH outcomes AS (
                    SELECT COUNT(*) AS total,
                        COUNT(*) FILTER (WHERE sync_status = 'succeeded') AS succeeded,
                        COUNT(*) FILTER (WHERE sync_status = 'failed') AS failed,
                        COUNT(*) FILTER (WHERE sync_status = 'unknown') AS unknown,
                        COUNT(*) FILTER (WHERE sync_status IN ('pending', 'in_progress')) AS unfinished
                    FROM public.patientdetails_sync_history WHERE batch_id = @batchId
                )
                UPDATE public.patientdetails_sync_batches AS b
                SET completed_at = clock_timestamp(),
                    batch_status = CASE
                        WHEN o.total = 0 THEN @emptyStatus
                        WHEN o.unknown > 0 OR @runUnknown THEN 'unknown'
                        WHEN o.succeeded = o.total AND @runFailed THEN 'partial_failed'
                        WHEN o.succeeded = o.total THEN 'succeeded'
                        WHEN o.failed = o.total THEN 'failed'
                        ELSE 'partial_failed' END
                FROM outcomes AS o
                WHERE b.id = @batchId AND b.batch_status = 'in_progress' AND o.unfinished = 0
                """,
                [
                    new NpgsqlParameter("batchId", batchId!.Value),
                    new NpgsqlParameter("emptyStatus", result is null ? "unknown" : result.Success ? "succeeded" : "failed"),
                    new NpgsqlParameter("runUnknown", result is null),
                    new NpgsqlParameter("runFailed", result is { Success: false })
                ], timeout.Token);
            if (affected != 1)
                logger.LogError("Unable to complete synchronization batch {BatchId}.", batchId);
            return affected == 1;
        }
        catch (Exception exception) when (exception is NpgsqlException or OperationCanceledException)
        {
            logger.LogError(exception, "Unable to persist synchronization batch {BatchId}. It requires reconciliation.", batchId);
            return false;
        }
    }
}
