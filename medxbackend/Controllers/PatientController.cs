using System.Security.Claims;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using medxbackend.Data;
using medxbackend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace medxbackend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class PatientController : ControllerBase
{
    private readonly DbHelper _dbHelper;

    /// <summary>初始化患者控制器及数据库访问帮助器。</summary>
    public PatientController(DbHelper dbHelper)
    {
        _dbHelper = dbHelper;
    }

    /// <summary>按请求条件获取下一笔患者资料。</summary>
    [HttpPost("getNextPatientdetails")]
    public async Task<IActionResult> GetNextPatientDetails(
        [FromBody] GetNextPatientDetailsRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { message = "用户 ID 无效" });
        }

        try
        {
            var results = await _dbHelper.QueryAsync(
                """
                SELECT *
                FROM public.get_next_patientdetails(
                    p_formtype => @formtype::varchar,
                    p_userid => @userid::integer,
                    p_datereceived => @datereceived::timestamp without time zone,
                    p_dailyno => @dailyno::varchar,
                    p_inputtype => @inputtype::integer
                )
                """,
                MapCurrentRow,
                [
                    Parameter("formtype", request.FormType),
                    Parameter("userid", userId),
                    Parameter("datereceived", request.DateReceived),
                    Parameter("dailyno", request.DailyNo),
                    Parameter("inputtype", request.InputType)
                ],
                cancellationToken);

            if (results.Count == 0)
            {
                return NotFound(new { message = "找不到下一筆患者資料" });
            }

            if (results.Count > 1)
            {
                return Conflict(new { message = "取得了多筆下一位患者資料，請稍後重試" });
            }

            return Ok(results[0]);
        }
        catch (PostgresException exception) when (exception.SqlState == "P0001")
        {
            return BadRequest(new { message = exception.MessageText });
        }
    }

    /// <summary>获取不同输入类型患者资料之间的字段差异。</summary>
    [HttpGet("getPatientdetailsFieldDifferences")]
    public async Task<ActionResult<IReadOnlyList<PatientDetailsFieldDifference>>>
        GetPatientDetailsFieldDifferences(CancellationToken cancellationToken)
    {
        var results = await _dbHelper.QueryAsync(
            """
            SELECT dailyno, inputtype_a, inputtype_b, status, all_fields_equal,
                   differing_fields, formtype_a, formtype_b
            FROM public.get_patientdetails_field_differences()
            ORDER BY dailyno DESC
            """,
            reader => new PatientDetailsFieldDifference(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.IsDBNull(5) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)
                ),
            cancellationToken: cancellationToken);

        return Ok(results);
    }

    /// <summary>按状态导出最终患者资料为 Excel 或 CSV。</summary>
    [HttpPost("exportPatientdetailsFinal")]
    public async Task<IActionResult> ExportPatientDetailsFinal(
        [FromBody] ExportPatientDetailsFinalRequest request,
        CancellationToken cancellationToken = default)
    {
        var statusValues = request.Statuses?
            .SelectMany(status => (status ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

        if (statusValues.Length == 0)
        {
            return BadRequest(new { message = "status 不能为空" });
        }

        var exportFormat = request.Format?.Trim().ToLowerInvariant() ?? "excel";
        if (exportFormat is not ("excel" or "csv"))
        {
            return BadRequest(new { message = "format 只支持 excel 或 csv" });
        }

        var statusParameters = statusValues
            .Select((status, index) => Parameter($"status{index}", status))
            .ToArray();
        var statusPlaceholders = string.Join(", ", statusValues
            .Select((_, index) => $"@status{index}"));
        var rows = await _dbHelper.QueryAsync(
            $"SELECT * FROM public.patientdetails_final WHERE status IN ({statusPlaceholders}) ORDER BY dailyno",
            MapCurrentRow,
            statusParameters,
            cancellationToken);

        if (rows.Count == 0)
        {
            return NotFound(new { message = "没有符合状态的最终患者资料" });
        }

        AssignExportCentreIds(rows);

        var columns = rows[0].Keys.ToArray();
        var filenameStatus = string.Join("_", statusValues.Select(status => string.Concat(status.Where(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_'))));
        filenameStatus = filenameStatus[..Math.Min(filenameStatus.Length, 80)];
        if (string.IsNullOrEmpty(filenameStatus))
        {
            filenameStatus = "status";
        }

        if (exportFormat == "csv")
        {
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", columns.Select(EscapeCsvField)));
            foreach (var row in rows)
            {
                csv.AppendLine(string.Join(",", columns.Select(column =>
                    EscapeCsvField(FormatExportValue(row[column])))));
            }

            return File(
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString()),
                "text/csv; charset=utf-8",
                $"patientdetails_final_{filenameStatus}.csv");
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("patientdetails_final");
        for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            worksheet.Cell(1, columnIndex + 1).Value = columns[columnIndex];
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
            {
                var value = rows[rowIndex][columns[columnIndex]];
                worksheet.Cell(rowIndex + 2, columnIndex + 1).Value =
                    XLCellValue.FromObject(FormatExcelValue(value), CultureInfo.InvariantCulture);
            }
        }

        worksheet.Row(1).Style.Font.Bold = true;
        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"patientdetails_final_{filenameStatus}.xlsx");
    }

    /// <summary>通过数据库函数新增患者资料并返回处理结果。</summary>
    [HttpPost("insertPatientdetails")]
    public async Task<IActionResult> InsertPatientDetails(
        [FromBody] CreatePatientDetailRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid User ID" });
        }

        try
        {
            var results = await _dbHelper.QueryAsync(
                """
                SELECT result_status, (result_data).*
                FROM public.insert_patientdetails(
                    p_patientnameen => @patientnameen::varchar,
                    p_patientnamechi => @patientnamechi::varchar,
                    p_gender => @gender::varchar,
                    p_dateofbirth => @dateofbirth::date,
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
                    p_inputtype => @inputtype::integer,
                    p_userid => @userid::integer,
                    p_dailyno => @dailyno::varchar,
                    p_productcode => @productcode::text[],
                    p_ivf => @ivf::integer,
                    p_redo => @redo::text,
                    p_hxofabortion => @hxofabortion::integer,
                    p_hold => @hold::integer,
                    p_tbc => @tbc::integer
                )
                """,
                MapCurrentRow,
                [
                    Parameter("patientnameen", request.PatientNameEn),
                    Parameter("patientnamechi", request.PatientNameChi),
                    Parameter("gender", request.Gender),
                    Parameter("dateofbirth", request.DateOfBirth),
                    Parameter("age", request.Age),
                    Parameter("hkidno", request.HkidNo),
                    Parameter("passportno", request.PassportNo),
                    Parameter("mobile", request.Mobile),
                    Parameter("refcustordr", request.RefCustOrDr),
                    Parameter("patientrefno", request.PatientRefNo),
                    Parameter("billtocustordr", request.BillToCustOrDr),
                    Parameter("speciment", request.Speciment),
                    Parameter("remarks", request.Remarks),
                    Parameter("datereceived", request.DateReceived),
                    Parameter("datecollected", request.DateCollected),
                    Parameter("gestationalweek", request.GestationalWeek),
                    Parameter("gestationalday", request.GestationalDay),
                    Parameter("pregnancy", request.Pregnancy),
                    Parameter("ethnicorigin", request.EthnicOrigin),
                    Parameter("formtype", request.FormType),
                    Parameter("inputtype", request.InputType),
                    Parameter("userid", userId),
                    Parameter("dailyno", request.DailyNo),
                    Parameter("productcode", request.ProductCode),
                    Parameter("ivf", request.Ivf),
                    Parameter("redo", request.Redo),
                    Parameter("hxofabortion", request.HxOfAbortion),
                    Parameter("hold", request.Hold),
                    Parameter("tbc", request.Tbc)
                ],
                cancellationToken);

            if (results.Count == 0)
            {
                return BadRequest(new { message = "建立患者資料失敗，未取得回傳結果" });
            }

            if (results.Count > 1)
            {
                return Conflict(new { message = "建立患者資料時取得多筆回傳結果" });
            }

            var result = results[0];
            if (string.Equals(result["result_status"]?.ToString(), "already exists", StringComparison.Ordinal))
            {
                return Conflict(new { message = "Patient data already exists." });
            }

            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (PostgresException exception) when (exception.SqlState == "P0001")
        {
            return BadRequest(new { message = exception.MessageText });
        }
    }

    /// <summary>通过数据库函数新增最终患者资料并返回处理结果。</summary>
    [HttpPost("insertPatientdetailsFinal")]
    public async Task<IActionResult> InsertPatientDetailsFinal(
        [FromBody] CreatePatientDetailsFinalRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid User ID" });
        }

        try
        {
            var results = await _dbHelper.QueryAsync(
                """
                SELECT result_status, (result_data).*
                FROM public.insert_patientdetails_final(
                    p_patientnameen => @patientnameen::varchar,
                    p_patientnamechi => @patientnamechi::varchar,
                    p_gender => @gender::varchar,
                    p_dateofbirth => @dateofbirth::date,
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
                    p_dailyno => @dailyno::varchar,
                    p_centreid => @centreid::varchar,
                    p_userid => @userid::integer,
                    p_productcode => @productcode::text[],
                    p_ivf => @ivf::integer,
                    p_redo => @redo::text,
                    p_hxofabortion => @hxofabortion::integer,
                    p_hold => @hold::integer,
                    p_tbc => @tbc::integer
                )
                """,
                MapCurrentRow,
                [
                    Parameter("patientnameen", request.PatientNameEn),
                    Parameter("patientnamechi", request.PatientNameChi),
                    Parameter("gender", request.Gender),
                    Parameter("dateofbirth", request.DateOfBirth),
                    Parameter("age", request.Age),
                    Parameter("hkidno", request.HkidNo),
                    Parameter("passportno", request.PassportNo),
                    Parameter("mobile", request.Mobile),
                    Parameter("refcustordr", request.RefCustOrDr),
                    Parameter("patientrefno", request.PatientRefNo),
                    Parameter("billtocustordr", request.BillToCustOrDr),
                    Parameter("speciment", request.Speciment),
                    Parameter("remarks", request.Remarks),
                    Parameter("datereceived", request.DateReceived),
                    Parameter("datecollected", request.DateCollected),
                    Parameter("gestationalweek", request.GestationalWeek),
                    Parameter("gestationalday", request.GestationalDay),
                    Parameter("pregnancy", request.Pregnancy),
                    Parameter("ethnicorigin", request.EthnicOrigin),
                    Parameter("formtype", request.FormType),
                    Parameter("dailyno", request.DailyNo),
                    Parameter("centreid", request.CentreId),
                    Parameter("userid", userId),
                    Parameter("productcode", request.ProductCode),
                    Parameter("ivf", request.Ivf),
                    Parameter("redo", request.Redo),
                    Parameter("hxofabortion", request.HxOfAbortion),
                    Parameter("hold", request.Hold),
                    Parameter("tbc", request.Tbc)
                ],
                cancellationToken);

            if (results.Count == 0)
            {
                return BadRequest(new { message = "建立最终患者资料失败，未取得回传结果" });
            }

            if (results.Count > 1)
            {
                return Conflict(new { message = "建立最终患者资料时取得多笔回传结果" });
            }

            var result = results[0];
            if (string.Equals(result["result_status"]?.ToString(), "already exists", StringComparison.Ordinal))
            {
                return Conflict(new { message = "Patient final data already exists." });
            }

            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (PostgresException exception) when (exception.SqlState == "P0001")
        {
            return BadRequest(new { message = exception.MessageText });
        }
    }

    /// <summary>直接创建患者资料记录。</summary>
    [HttpPost("createPatientdetail")]
    public async Task<IActionResult> CreatePatientDetail(
        [FromBody] CreatePatientDetailRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid User ID" });
        }

        const string sql =
            """
            INSERT INTO public.patientdetails
            (
                centreid, dailyno, patientnameen, patientnamechi, gender, dateofbirth, age,
                hkidno, passportno, mobile, refcustordr, patientrefno, billtocustordr,
                speciment, remarks, datereceived, datecollected,
                gestationalweek, gestationalday, pregnancy, ethnicorigin, formtype, inputtype,
                userid, productcode, ivf, redo, hxofabortion, hold, tbc
            )
            VALUES
            (
                @centreid, @dailyno, @patientnameen, @patientnamechi, @gender, @dateofbirth, @age,
                @hkidno, @passportno, @mobile, @refcustordr, @patientrefno, @billtocustordr,
                @speciment, @remarks, @datereceived, @datecollected,
                @gestationalweek, @gestationalday, @pregnancy, @ethnicorigin, @formtype, @inputtype,
                @userid, @productcode, @ivf, @redo, @hxofabortion, @hold, @tbc
            )
            """;

        await _dbHelper.ExecuteNonQueryAsync(
            sql,
            [
                Parameter("centreid", request.CentreId),
                Parameter("dailyno", request.DailyNo),
                Parameter("patientnameen", request.PatientNameEn),
                Parameter("patientnamechi", request.PatientNameChi),
                Parameter("gender", request.Gender),
                Parameter("dateofbirth", request.DateOfBirth),
                Parameter("age", request.Age),
                Parameter("hkidno", request.HkidNo),
                Parameter("passportno", request.PassportNo),
                Parameter("mobile", request.Mobile),
                Parameter("refcustordr", request.RefCustOrDr),
                Parameter("patientrefno", request.PatientRefNo),
                Parameter("billtocustordr", request.BillToCustOrDr),
                Parameter("speciment", request.Speciment),
                Parameter("remarks", request.Remarks),
                Parameter("datereceived", request.DateReceived),
                Parameter("datecollected", request.DateCollected),
                Parameter("gestationalweek", request.GestationalWeek),
                Parameter("gestationalday", request.GestationalDay),
                Parameter("pregnancy", request.Pregnancy),
                Parameter("ethnicorigin", request.EthnicOrigin),
                Parameter("formtype", request.FormType),
                Parameter("inputtype", request.InputType),
                Parameter("userid", userId),
                Parameter("productcode", request.ProductCode),
                Parameter("ivf", request.Ivf),
                Parameter("redo", request.Redo),
                Parameter("hxofabortion", request.HxOfAbortion),
                Parameter("hold", request.Hold),
                Parameter("tbc", request.Tbc)
            ],
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            centreId = request.CentreId,
            dailyNo = request.DailyNo,
            message = "患者资料创建成功"
        });
    }

    /// <summary>按每日编号末尾部分查询患者资料。</summary>
    [HttpGet("getPatientdetail")]
    public async Task<IActionResult> GetByLabNo(
        [FromQuery] string dailyNo,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dailyNo))
        {
            return BadRequest(new { message = "dailyNo 不能为空" });
        }

        var dailyNoSuffix = dailyNo.Trim();
        if (dailyNoSuffix.Length > 5)
        {
            dailyNoSuffix = dailyNoSuffix[^5..];
        }

        var patients = await _dbHelper.QueryAsync(
            "SELECT * FROM public.patientdetails WHERE RIGHT(dailyno, 5) = @dailyNoSuffix",
            reader =>
            {
                var patient = new Dictionary<string, object?>();
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    var value = reader.GetValue(index);
                    patient[reader.GetName(index)] = value is DBNull ? null : value;
                }

                return patient;
            },
            [new NpgsqlParameter("dailyNoSuffix", dailyNoSuffix)],
            cancellationToken);

        if (patients.Count == 0)
        {
            return NotFound(new { message = "找不到对应的患者资料" });
        }

        return Ok(patients);
    }

    /// <summary>按每日编号、表单类型、输入类型及用户查询患者资料。</summary>
    [HttpPost("getPatientdetailsByDailyno")]
    public async Task<IActionResult> GetPatientDetailsByDailyNo(
        [FromBody] GetPatientDetailsByDailynoRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DailyNo))
        {
            return BadRequest(new { message = "dailyNo 不能为空" });
        }

        var patients = await _dbHelper.QueryAsync(
            """
            SELECT *
            FROM public.get_patientdetails_by_dailyno(
                p_dailyno => @dailyno::varchar,
                p_formtype => @formtype::varchar,
                p_inputtype => @inputtype::integer,
                p_userid => @userid::integer
            )
            """,
            MapCurrentRow,
            [
                Parameter("dailyno", request.DailyNo.Trim()),
                Parameter("formtype", request.FormType),
                Parameter("inputtype", request.InputType),
                Parameter("userid", request.UserId)
            ],
            cancellationToken);

        if (patients.Count == 0)
        {
            return NotFound(new { message = "找不到对应的患者资料" });
        }

        return Ok(patients);
    }

    /// <summary>按每日编号获取上一笔或下一笔患者资料。</summary>
    [HttpPost("getAdjacentPatientdetails")]
    public async Task<IActionResult> GetAdjacentPatientDetails(
        [FromBody] GetAdjacentPatientDetailsRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { message = "用户 ID 无效" });
        }

        var direction = request.Direction?.Trim().ToLowerInvariant();
        if (direction is not ("previous" or "next"))
        {
            return BadRequest(new { message = "direction 必须为 previous 或 next" });
        }

        try
        {
            var patients = await _dbHelper.QueryAsync(
                """
                SELECT *
                FROM public.get_adjacent_patientdetails(
                    p_dailyno => @dailyno::varchar,
                    p_direction => @direction::varchar,
                    p_formtype => @formtype::varchar,
                    p_inputtype => @inputtype::integer,
                    p_userid => @userid::integer
                )
                """,
                MapCurrentRow,
                [
                    Parameter("dailyno", request.DailyNo?.Trim()),
                    Parameter("direction", direction),
                    Parameter("formtype", request.FormType),
                    Parameter("inputtype", request.InputType),
                    Parameter("userid", userId)
                ],
                cancellationToken);

            if (patients.Count == 0)
            {
                return NotFound(new { message = "找不到相邻的患者资料" });
            }

            return Ok(patients[0]);
        }
        catch (PostgresException exception) when (exception.SqlState == "P0001")
        {
            return BadRequest(new { message = exception.MessageText });
        }
    }

    /// <summary>按输入类型和表单类型查询患者资料。</summary>
    [HttpPost("getPatientdetailsByInputtypeAndFormtype")]
    public async Task<IActionResult> GetPatientDetailsByInputTypeAndFormType(
        [FromBody] GetPatientDetailsByTypeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FormType))
        {
            return BadRequest(new { message = "formType 不能为空" });
        }

        if (request.InputType is null)
        {
            return BadRequest(new { message = "inputType 不能为空" });
        }

        var patients = await _dbHelper.QueryAsync(
            """
            SELECT *
            FROM public.patientdetails
            WHERE inputtype = @inputtype
              AND formtype = @formtype
            ORDER BY dailyno DESC
            """,
            MapCurrentRow,
            [
                Parameter("inputtype", request.InputType),
                Parameter("formtype", request.FormType.Trim())
            ],
            cancellationToken);

        return Ok(patients);
    }

    /// <summary>按每日编号和输入类型删除患者资料。</summary>
    [HttpPost("deletePatientdetailsByDailyno")]
    public async Task<IActionResult> DeletePatientDetailsByDailyNo(
        [FromBody] DeletePatientDetailsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DailyNo))
        {
            return BadRequest(new { message = "dailyNo 不能为空" });
        }

        if (request.InputType is null)
        {
            return BadRequest(new { message = "inputType 不能为空" });
        }

        var dailyNo = request.DailyNo.Trim();
        var deletedCount = await _dbHelper.ExecuteNonQueryAsync(
            "DELETE FROM public.patientdetails WHERE dailyno = @dailyno AND inputtype = @inputtype",
            [
                Parameter("dailyno", dailyNo),
                Parameter("inputtype", request.InputType)
            ],
            cancellationToken);

        if (deletedCount == 0)
        {
            return NotFound(new { message = "找不到对应的患者资料" });
        }

        return Ok(new
        {
            message = "患者资料已删除",
            dailyNo,
            inputType = request.InputType,
            deletedCount
        });
    }

    /// <summary>在收到明确确认后清空患者资料。</summary>
    [HttpPost("resetPatientdetails")]
    public async Task<IActionResult> ResetPatientDetails(
        [FromBody] ResetPatientDetailsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Confirm is not true)
        {
            return BadRequest(new { message = "必须传入 confirm=true 才能清空患者资料" });
        }

        try
        {
            var results = await _dbHelper.QueryAsync(
                "SELECT patientdetails_deleted, patientdetails_final_deleted FROM public.reset_patientdetails(@confirm)",
                reader => new
                {
                    patientdetailsDeleted = reader.GetInt64(0),
                    patientdetailsFinalDeleted = reader.GetInt64(1)
                },
                [Parameter("confirm", request.Confirm)],
                cancellationToken);

            if (results.Count == 0)
            {
                return BadRequest(new { message = "清空患者资料失败，未取得执行结果" });
            }

            return Ok(new
            {
                message = "患者资料已全部清空",
                results[0].patientdetailsDeleted,
                results[0].patientdetailsFinalDeleted
            });
        }
        catch (PostgresException exception) when (exception.SqlState == "P0001")
        {
            return BadRequest(new { message = exception.MessageText });
        }
    }

    /// <summary>按每日编号更新患者资料并处理匹配状态。</summary>
    [HttpPost("updatePatientdetailsByDailyno")]
    public async Task<IActionResult> UpdatePatientDetailsByDailyNo(
        [FromBody] UpdatePatientDetailsRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid User ID" });
        }

        if (string.IsNullOrWhiteSpace(request.DailyNo))
        {
            return BadRequest(new { message = "dailyNo 不能为空" });
        }

        try
        {
            var results = await _dbHelper.QueryAsync(
                """
                SELECT result_status, (result_data).*
                FROM public.update_patientdetails_by_dailyno(
                    p_patientnameen => @patientnameen::varchar,
                    p_patientnamechi => @patientnamechi::varchar,
                    p_gender => @gender::varchar,
                    p_dateofbirth => @dateofbirth::date,
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
                    p_inputtype => @inputtype::integer,
                    p_userid => @userid::integer,
                    p_dailyno => @dailyno::varchar,
                    p_productcode => @productcode::text[],
                    p_ivf => @ivf::integer,
                    p_redo => @redo::text,
                    p_hxofabortion => @hxofabortion::integer,
                    p_hold => @hold::integer,
                    p_tbc => @tbc::integer
                )
                """,
                MapCurrentRow,
                [
                    Parameter("patientnameen", request.PatientNameEn),
                    Parameter("patientnamechi", request.PatientNameChi),
                    Parameter("gender", request.Gender),
                    Parameter("dateofbirth", request.DateOfBirth),
                    Parameter("age", request.Age),
                    Parameter("hkidno", request.HkidNo),
                    Parameter("passportno", request.PassportNo),
                    Parameter("mobile", request.Mobile),
                    Parameter("refcustordr", request.RefCustOrDr),
                    Parameter("patientrefno", request.PatientRefNo),
                    Parameter("billtocustordr", request.BillToCustOrDr),
                    Parameter("speciment", request.Speciment),
                    Parameter("remarks", request.Remarks),
                    Parameter("datereceived", request.DateReceived),
                    Parameter("datecollected", request.DateCollected),
                    Parameter("gestationalweek", request.GestationalWeek),
                    Parameter("gestationalday", request.GestationalDay),
                    Parameter("pregnancy", request.Pregnancy),
                    Parameter("ethnicorigin", request.EthnicOrigin),
                    Parameter("formtype", request.FormType),
                    Parameter("inputtype", request.InputType),
                    Parameter("userid", userId),
                    Parameter("dailyno", request.DailyNo.Trim()),
                    Parameter("productcode", request.ProductCode),
                    Parameter("ivf", request.Ivf),
                    Parameter("redo", request.Redo),
                    Parameter("hxofabortion", request.HxOfAbortion),
                    Parameter("hold", request.Hold),
                    Parameter("tbc", request.Tbc)
                ],
                cancellationToken);

            if (results.Count == 0)
            {
                return NotFound(new { message = "找不到对应的患者资料" });
            }

            var result = results[0];
            var status = result["result_status"]?.ToString();

            if (string.Equals(status, "not_found", StringComparison.Ordinal))
            {
                return NotFound(new { message = "找不到对应的患者资料" });
            }

            if (string.Equals(status, "multiple_matches", StringComparison.Ordinal))
            {
                return Conflict(new { message = "找到多笔患者资料，无法确定更新目标" });
            }

            return Ok(result);
        }
        catch (PostgresException exception) when (exception.SqlState == "P0001")
        {
            return BadRequest(new { message = exception.MessageText });
        }
    }

    /// <summary>获取按编号排序的种族选项。</summary>
    [HttpGet("getEthnicOriginOptions")]
    public Task<IActionResult> GetEthnicOriginOptions(
        CancellationToken cancellationToken)
    {
        return GetOptionsAsync(
            "SELECT id, description FROM public.ethnicoriginoptions ORDER BY id",
            cancellationToken);
    }

    /// <summary>获取按编号排序的妊娠选项。</summary>
    [HttpGet("getPregnancyOptions")]
    public Task<IActionResult> GetPregnancyOptions(
        CancellationToken cancellationToken)
    {
        return GetOptionsAsync(
            "SELECT id, description FROM public.pregnancyoptions ORDER BY id",
            cancellationToken);
    }

    /// <summary>获取按编号排序的样本类型选项。</summary>
    [HttpGet("getSpecimenOptions")]
    public Task<IActionResult> GetSpecimenOptions(
        CancellationToken cancellationToken)
    {
        return GetOptionsAsync(
            "SELECT id, description FROM public.specimenoptions ORDER BY id",
            cancellationToken);
    }

    /// <summary>执行选项查询并返回选项编号和说明。</summary>
    private async Task<IActionResult> GetOptionsAsync(
        string sql,
        CancellationToken cancellationToken)
    {
        var options = await _dbHelper.QueryAsync(
            sql,
            reader => new OptionItem(
                reader.GetInt32(0),
                reader.GetString(1)),
            cancellationToken: cancellationToken);

        return Ok(options);
    }

    /// <summary>从当前用户声明中读取并解析用户编号。</summary>
    private bool TryGetUserId(out int userId)
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    /// <summary>将数据库读取器当前行转换为字典，并将数据库空值映射为 null。</summary>
    private static Dictionary<string, object?> MapCurrentRow(NpgsqlDataReader reader)
    {
        var result = new Dictionary<string, object?>();
        for (var index = 0; index < reader.FieldCount; index++)
        {
            var value = reader.GetValue(index);
            result[reader.GetName(index)] = value is DBNull ? null : value;
        }

        return result;
    }

    /// <summary>建立数据库参数，并将空值转换为数据库空值。</summary>
    private static NpgsqlParameter Parameter(string name, object? value)
    {
        return new NpgsqlParameter(name, value ?? DBNull.Value);
    }

    private static string EscapeCsvField(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '@' or '\t' or '\r')
        {
            value = $"'{value}";
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string FormatExportValue(object? value)
    {
        return value switch
        {
            null or DBNull => string.Empty,
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            Array values => string.Join(", ", values.Cast<object?>().Select(FormatExportValue)),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static object? FormatExcelValue(object? value)
    {
        return value is Array ? FormatExportValue(value) : value is DBNull ? null : value;
    }

    private static void AssignExportCentreIds(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var sequenceByFormAndMonth = new Dictionary<(string FormType, string YearMonth), int>();

        foreach (var row in rows)
        {
            var formType = row.GetValueOrDefault("formtype")?.ToString()?.Trim() ?? string.Empty;
            var centreIdPrefix = formType switch
            {
                "DC" => "MC",
                "DR" => "MR",
                _ => formType
            };
            var yearMonth = row.GetValueOrDefault("datereceived") switch
            {
                DateTime dateTime => dateTime.ToString("yyMM", CultureInfo.InvariantCulture),
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyMM", CultureInfo.InvariantCulture),
                _ => null
            };

            if (yearMonth is null)
            {
                var dailyNo = row.GetValueOrDefault("dailyno")?.ToString();
                var dailyNoDateOffset = formType.Length;
                if (dailyNo is not null
                    && dailyNo.Length >= dailyNoDateOffset + 4
                    && dailyNo.AsSpan(dailyNoDateOffset, 4).IndexOfAnyExceptInRange('0', '9') < 0)
                {
                    yearMonth = dailyNo.Substring(dailyNoDateOffset, 4);
                }
                else
                {
                    throw new InvalidOperationException(
                        "无法从 datereceived 或 dailyno 生成 centreid 的年月部分。");
                }
            }

            var key = (centreIdPrefix, yearMonth);
            var sequence = sequenceByFormAndMonth.GetValueOrDefault(key) + 1;
            sequenceByFormAndMonth[key] = sequence;
            row["centreid"] = $"{centreIdPrefix}{yearMonth}-{sequence:D5}";
        }
    }

    private sealed record OptionItem(int Id, string Description);

}
