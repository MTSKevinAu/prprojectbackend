using medxbackend.Data;
using Npgsql;

namespace medxbackend.Services;

public sealed class PatientDetailsExportService(DbHelper dbHelper) : IPatientDetailsExportService
{
    public static string[] NormalizeStatuses(string[]? statuses) =>
        statuses?
            .SelectMany(status => (status ?? string.Empty).Split(
                ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

    public Task<List<Dictionary<string, object?>>> GetRowsAsync(
        string[] statuses, CancellationToken cancellationToken) =>
        dbHelper.QueryAsync(
            "SELECT * FROM public.export_patientdetails_final(@statuses::text[])",
            MapRow,
            [new NpgsqlParameter("statuses", statuses)],
            cancellationToken);

    public Task<List<Dictionary<string, object?>>> GetRowsByCentreIdAsync(
        string centreId, string targetSystem, CancellationToken cancellationToken) =>
        dbHelper.QueryAsync(
            """
            SELECT
                pf.centreid::text AS "Lab No.",
                pf.dailyno::text AS "Daily No.",
                pf.patientnameen::text AS "English Name",
                pf.patientnamechi::text AS "Chinese Name",
                array_to_string(pf.productcode, ', ') AS "Product Code",
                to_char(pf.dateofbirth, 'DD/MM/YYYY') AS "Date of Birth",
                pf.age::integer AS "Maternal Age",
                CASE
                    WHEN pf.gestationalweek IS NULL THEN '1'
                    WHEN (pf.gestationalweek::text || '.' || COALESCE(pf.gestationalday::text, '0'))::numeric < 1 THEN '1'
                    ELSE COALESCE(pf.gestationalweek::text, '') || '.' || COALESCE(pf.gestationalday::text, '')
                END AS "Gestational Week",
                pf.pregnancy::text AS "Pregnancy",
                pf.ethnicorigin::text AS "Ethnic Origin",
                COALESCE(NULLIF(btrim(pf.hkidno), ''), NULLIF(btrim(pf.passportno), '')) AS "ID/Passport No.",
                pf.refcustordr::text AS "Clinic Code",
                pf.speciment::text AS "Sample Type",
                pf.patientrefno::text AS "Patient Ref No.",
                to_char(pf.datereceived, 'YYYY-MM-DD') AS "Date Received",
                to_char(pf.datecollected, 'YYYY-MM-DD') AS "Date Collected",
                CASE pf.tbc WHEN 1 THEN 'T' WHEN 0 THEN '' ELSE pf.tbc::text END AS "TBC Status",
                CASE pf.hold WHEN 1 THEN 'H' WHEN 0 THEN '' ELSE pf.hold::text END AS "Hold Status",
                CASE pf.hxofabortion WHEN 1 THEN TRUE WHEN 0 THEN FALSE END AS "History of Abortion",
                CASE pf.ivf WHEN 1 THEN TRUE WHEN 0 THEN FALSE END AS "IVF",
                pf.remarks::text AS "Remarks"
            FROM public.patientdetails_final AS pf
            WHERE pf.centreid = @centreId
              AND EXISTS (
                  SELECT 1
                  FROM public.patientdetails_sync AS sync
                  WHERE sync.centreid = pf.centreid
                    AND sync.formtype = pf.formtype
                    AND sync.target_system = @targetSystem
              )
            ORDER BY pf.dailyno
            """,
            MapRow,
            [
                new NpgsqlParameter("centreId", centreId),
                new NpgsqlParameter("targetSystem", targetSystem)
            ],
            cancellationToken);

    private static Dictionary<string, object?> MapRow(NpgsqlDataReader reader)
    {
        var row = new Dictionary<string, object?>();
        for (var index = 0; index < reader.FieldCount; index++)
        {
            row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
        }

        return row;
    }
}
