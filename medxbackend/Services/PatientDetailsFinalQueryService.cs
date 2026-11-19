using System.Text.Json;
using medxbackend.Data;
using Npgsql;

namespace medxbackend.Services;

public sealed class PatientDetailsFinalQueryService(DbHelper dbHelper) : IPatientDetailsFinalQueryService
{
    public async Task<IReadOnlyList<JsonElement>> GetByDailyNo(
        string dailyNo, CancellationToken cancellationToken) =>
        await dbHelper.QueryAsync(
            """
            SELECT to_jsonb(patient)::text
            FROM public.patientdetails_final AS patient
            WHERE patient.dailyno = @dailyno
            LIMIT 2
            """,
            reader => JsonSerializer.Deserialize<JsonElement>(reader.GetString(0)),
            [new NpgsqlParameter("dailyno", dailyNo.Trim())],
            cancellationToken);
}
