using System.Text.Json;
using medxbackend.Data;
using medxbackend.Models;
using Npgsql;

namespace medxbackend.Services;

public sealed class PatientDetailsFinalUpdateService(DbHelper dbHelper) : IPatientDetailsFinalUpdateService
{
    public async Task<JsonElement> Update(
        UpdatePatientDetailsFinalRequest request, int userId, CancellationToken cancellationToken)
    {
        var results = await dbHelper.QueryAsync(
            """
            SELECT to_jsonb(updated)::text
            FROM (
                SELECT result_status, (result_data).*
                FROM public.update_patientdetails_final_by_dailyno(
                    p_patientnameen => @patientnameen::varchar,
                    p_patientnamechi => @patientnamechi::varchar,
                    p_gender => @gender::varchar,
                    p_dateofbirth => @dateofbirth::timestamp without time zone,
                    p_age => @age::integer,
                    p_hkidno => @hkidno::varchar,
                    p_passportno => @passportno::varchar,
                    p_mobile => @mobile::varchar,
                    p_refcustordr => @refcustordr::varchar,
                    p_patientrefno => @patientrefno::varchar,
                    p_billtocustordr => @billtocustordr::varchar,
                    p_speciment => @speciment::varchar,
                    p_remarks => @remarks::varchar,
                    p_datereceived => @datereceived::timestamp without time zone,
                    p_datecollected => @datecollected::timestamp without time zone,
                    p_gestationalweek => @gestationalweek::integer,
                    p_gestationalday => @gestationalday::integer,
                    p_pregnancy => @pregnancy::varchar,
                    p_ethnicorigin => @ethnicorigin::varchar,
                    p_formtype => @formtype::varchar,
                    p_userid => @userid::integer,
                    p_dailyno => @dailyno::varchar,
                    p_productcode => @productcode::text[],
                    p_ivf => @ivf::integer,
                    p_redo => @redo::text,
                    p_hxofabortion => @hxofabortion::integer,
                    p_hold => @hold::integer,
                    p_tbc => @tbc::integer)
            ) AS updated
            """,
            reader => JsonSerializer.Deserialize<JsonElement>(reader.GetString(0)),
            [
                Parameter("patientnameen", request.PatientNameEn),
                Parameter("patientnamechi", request.PatientNameChi),
                Parameter("gender", request.Gender),
                Parameter("dateofbirth", request.DateOfBirth?.DateTime),
                Parameter("age", request.Age),
                Parameter("hkidno", request.HkidNo),
                Parameter("passportno", request.PassportNo),
                Parameter("mobile", request.Mobile),
                Parameter("refcustordr", request.RefCustOrDr),
                Parameter("patientrefno", request.PatientRefNo),
                Parameter("billtocustordr", request.BillToCustOrDr),
                Parameter("speciment", request.Speciment),
                Parameter("remarks", request.Remarks),
                Parameter("datereceived", request.DateReceived?.DateTime),
                Parameter("datecollected", request.DateCollected?.DateTime),
                Parameter("gestationalweek", request.GestationalWeek),
                Parameter("gestationalday", request.GestationalDay),
                Parameter("pregnancy", request.Pregnancy),
                Parameter("ethnicorigin", request.EthnicOrigin),
                Parameter("formtype", request.FormType.Trim()),
                Parameter("userid", userId),
                Parameter("dailyno", request.DailyNo.Trim()),
                Parameter("productcode", request.ProductCode),
                Parameter("ivf", request.Ivf),
                Parameter("redo", request.Redo),
                Parameter("hxofabortion", request.HxOfAbortion),
                Parameter("hold", request.Hold),
                Parameter("tbc", request.Tbc)
            ], cancellationToken);
        if (results.Count != 1)
        {
            throw new InvalidOperationException("Final patient update did not return exactly one result.");
        }

        return results[0];
    }

    private static NpgsqlParameter Parameter(string name, object? value) => new(name, value ?? DBNull.Value);
}
