using medxbackend.Data;
using medxbackend.Models;
using Npgsql;
using NpgsqlTypes;

namespace medxbackend.Services;

public sealed class CrmPreviewService(DbHelper dbHelper) : ICrmPreviewService
{
    public async Task<CrmBatchPreviewResponse> PrepareAsync(
        CrmPreviewRequest request, CancellationToken cancellationToken)
    {
        var formTypes = request.FormTypes.Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        await dbHelper.ExecuteNonQueryAsync(
            """
            SELECT * FROM public.update_patientdetails_final_centreid(
                p_target_system => 'CRM',
                p_sequence_date => COALESCE(@sequenceDate::date, CURRENT_DATE),
                p_formtypes => @formTypes,
                p_statuses => ARRAY[@status]::text[],
                p_source_table => 'patientdetails_final')
            """,
            [
                new NpgsqlParameter("sequenceDate", NpgsqlDbType.Date)
                {
                    Value = (object?)request.SequenceDate ?? DBNull.Value
                },
                new NpgsqlParameter("formTypes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = formTypes },
                new NpgsqlParameter("status", request.Status.Trim())
            ], cancellationToken);

        var records = await dbHelper.QueryAsync(
            """
            SELECT id, target_system, centreid, formtype, sequence_date,
                   sync_status, attempt_count, last_attempt_at, synced_at,
                   remote_record_id, last_http_status, last_error, created_at
            FROM public.patientdetails_sync
            WHERE target_system = 'CRM'
              AND sequence_date = COALESCE(@sequenceDate::date, CURRENT_DATE)
              AND formtype = ANY(@formTypes)
            ORDER BY id
            """,
            reader => new CrmSyncRecordResponse(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetFieldValue<DateTimeOffset>(12)),
            [
                new NpgsqlParameter("sequenceDate", NpgsqlDbType.Date)
                {
                    Value = (object?)request.SequenceDate ?? DBNull.Value
                },
                new NpgsqlParameter("formTypes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = formTypes }
            ], cancellationToken);
        return new CrmBatchPreviewResponse(records.Count, records);
    }
}
