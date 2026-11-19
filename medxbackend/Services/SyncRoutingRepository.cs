using medxbackend.Data;
using medxbackend.Models;
using Npgsql;
using NpgsqlTypes;

namespace medxbackend.Services;

public sealed class SyncRoutingRepository(DbHelper dbHelper) : ISyncRoutingRepository
{
    public async Task<RecentSyncResponse> LoadRecent(
        int? count, string? targetSystem, CancellationToken cancellationToken)
    {
        var rows = await dbHelper.QueryAsync(
            """
            WITH latest AS (
                SELECT id, batch_status, started_at, completed_at
                FROM public.patientdetails_sync_batches
                WHERE completed_at IS NOT NULL
                ORDER BY completed_at DESC, id DESC
                LIMIT 1
            ), attempts AS (
                SELECT h.*
                FROM public.get_latest_patientdetails_sync_batch(p_target_system => @targetSystem) AS h
                ORDER BY h.attempted_at DESC NULLS LAST, h.history_id DESC
                LIMIT @count
            )
            SELECT b.id, h.target_system, b.batch_status, b.started_at, b.completed_at,
                   q.id, q.centreid, q.formtype, h.sync_status, q.attempt_count,
                   h.attempted_at, h.synced_at, h.remote_record_id, h.last_http_status, h.last_error,
                   h.dailyno,
                   h.history_id, h.completed_at
            FROM latest AS b
            LEFT JOIN attempts AS h ON h.batch_id = b.id
            LEFT JOIN public.patientdetails_sync AS q ON q.id = h.sync_id AND q.target_system = h.target_system
            WHERE @targetSystem IS NULL OR EXISTS (
                SELECT 1 FROM public.patientdetails_sync_history AS matched
                WHERE matched.batch_id = b.id AND matched.target_system = @targetSystem)
            ORDER BY h.attempted_at DESC NULLS LAST, h.history_id DESC NULLS LAST, b.id DESC
            """,
            reader => (
                Batch: new RecentSyncBatchResponse(reader.GetInt64(0), reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4)),
                Record: reader.IsDBNull(16) ? null : new RecentSyncRecordResponse(
                    reader.GetInt64(5), reader.GetString(1), reader.GetString(6), reader.GetString(7),
                    reader.GetString(8), reader.GetInt32(9),
                    reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                    reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetInt32(13),
                    reader.IsDBNull(14) ? null : reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15),
                    reader.GetInt64(16), reader.GetInt64(0),
                    reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17))),
            [
                new NpgsqlParameter("count", NpgsqlDbType.Integer) { Value = (object?)count ?? DBNull.Value },
                new NpgsqlParameter("targetSystem", NpgsqlDbType.Text) { Value = (object?)targetSystem ?? DBNull.Value }
            ], cancellationToken);
        var records = rows.Where(row => row.Record is not null).Select(row => row.Record!).ToList();
        return new RecentSyncResponse(records.Count, records,
            rows.Select(row => row.Batch).DistinctBy(batch => batch.Id).ToList());
    }

    public async Task<SyncStatisticsResponse> GetStatistics(CancellationToken cancellationToken)
    {
        var rows = await dbHelper.QueryAsync(
            """
            SELECT COUNT(*),
                   COUNT(*) FILTER (WHERE sync_status = 'pending'),
                   COUNT(*) FILTER (WHERE sync_status = 'succeeded'),
                   COUNT(*) FILTER (WHERE sync_status = 'failed')
            FROM public.patientdetails_sync
            """,
            reader => new SyncStatisticsResponse(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)),
            cancellationToken: cancellationToken);
        return rows.Single();
    }

    public Task<List<SyncRoutingRule>> LoadEnabledRules(CancellationToken cancellationToken) =>
        dbHelper.QueryAsync(
            """
            SELECT formtype, target_system
            FROM public.patientdetails_sync_rules
            WHERE is_enabled = true
            ORDER BY target_system, formtype
            """,
            reader => new SyncRoutingRule(reader.GetString(0), reader.GetString(1)),
            cancellationToken: cancellationToken);

    public async Task PrepareOrders(
        string target, DateOnly sequenceDate, string[] formTypes, CancellationToken cancellationToken)
    {
        await dbHelper.ExecuteNonQueryAsync(
            """
            SELECT * FROM public.update_patientdetails_final_centreid(
                p_target_system => @target,
                p_sequence_date => @sequenceDate,
                p_formtypes => @formTypes,
                p_statuses => ARRAY['Matched']::text[],
                p_source_table => 'patientdetails_final')
            """,
            [
                new NpgsqlParameter("target", target),
                new NpgsqlParameter("sequenceDate", NpgsqlDbType.Date) { Value = sequenceDate },
                new NpgsqlParameter("formTypes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = formTypes }
            ], cancellationToken);
    }

    public Task<List<SyncOrderPreviewItemResponse>> LoadPreview(
        DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken) =>
        dbHelper.QueryAsync(
            """
            SELECT id, target_system, centreid, formtype, dailyno,
                   sync_status, attempt_count, remote_record_id, last_http_status, last_error
            FROM public.get_patientdetails_sync_by_date(
                p_start_date => @startDate,
                p_end_date => @endDate)
            ORDER BY centreid, formtype, target_system, id
            """,
            reader => new SyncOrderPreviewItemResponse(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)),
            [
                new NpgsqlParameter("startDate", NpgsqlDbType.Date) { Value = startDate },
                new NpgsqlParameter("endDate", NpgsqlDbType.Date) { Value = endDate }
            ],
            cancellationToken);

    public Task<List<SyncRoutingPatient>> LoadPatients(string centreId, CancellationToken cancellationToken) =>
        dbHelper.QueryAsync(
            """
            SELECT centreid, formtype, dailyno
            FROM public.patientdetails_final
            WHERE centreid = @centreId
            LIMIT 2
            """,
            reader => new SyncRoutingPatient(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2)),
            [new NpgsqlParameter("centreId", centreId)], cancellationToken);

    public Task<List<string>> LoadTargets(string formType, CancellationToken cancellationToken) =>
        dbHelper.QueryAsync(
            """
            SELECT target_system
            FROM public.patientdetails_sync_rules
            WHERE formtype = @formType AND is_enabled = true
            ORDER BY target_system
            """,
            reader => reader.GetString(0),
            [new NpgsqlParameter("formType", formType)], cancellationToken);

    public async Task<SyncRoutingQueue> GetOrCreateQueue(
        SyncRoutingPatient patient, string target, CancellationToken cancellationToken)
    {
        await dbHelper.ExecuteNonQueryAsync(
            """
            INSERT INTO public.patientdetails_sync (target_system, centreid, formtype)
            VALUES (@target, @centreId, @formType)
            ON CONFLICT ON CONSTRAINT ux_patientdetails_sync_target_patient DO NOTHING
            """,
            [
                new NpgsqlParameter("target", target),
                new NpgsqlParameter("centreId", patient.CentreId),
                new NpgsqlParameter("formType", patient.FormType)
            ], cancellationToken);
        var rows = await dbHelper.QueryAsync(
            """
            SELECT id, sync_status, remote_record_id
            FROM public.patientdetails_sync
            WHERE target_system = @target AND centreid = @centreId AND formtype = @formType
            """,
            reader => new SyncRoutingQueue(reader.GetInt64(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)),
            [
                new NpgsqlParameter("target", target),
                new NpgsqlParameter("centreId", patient.CentreId),
                new NpgsqlParameter("formType", patient.FormType)
            ], cancellationToken);
        return rows.Count == 1 ? rows[0]
            : throw new InvalidOperationException("The selected synchronization queue record disappeared.");
    }

    public async Task<SyncRoutingQueue?> LoadQueue(
        SyncRoutingPatient patient, string target, CancellationToken cancellationToken)
    {
        var rows = await dbHelper.QueryAsync(
            """
            SELECT id, sync_status, remote_record_id FROM public.patientdetails_sync
            WHERE centreid = @centreId AND formtype = @formType AND target_system = @target
            """,
            reader => new SyncRoutingQueue(reader.GetInt64(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)),
            [
                new NpgsqlParameter("centreId", patient.CentreId),
                new NpgsqlParameter("formType", patient.FormType),
                new NpgsqlParameter("target", target)
            ], cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<bool> SaveLisTokenFailure(
        long syncId, int? httpStatus, string error, CancellationToken cancellationToken) =>
        await dbHelper.ExecuteNonQueryAsync(
            """
            UPDATE public.patientdetails_sync
            SET last_http_status = @httpStatus, last_error = @error
            WHERE id = @id AND target_system = 'LIS' AND sync_status IN ('pending', 'failed')
            """,
            [
                new NpgsqlParameter("id", syncId),
                new NpgsqlParameter("httpStatus", NpgsqlDbType.Integer) { Value = (object?)httpStatus ?? DBNull.Value },
                new NpgsqlParameter("error", error)
            ], cancellationToken) == 1;

}
