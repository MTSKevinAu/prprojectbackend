using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using medxbackend.Data;
using medxbackend.Models;
using medxbackend.Services;
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
    private readonly IPatientDetailsExportService _exportService;

    /// <summary>初始化患者控制器及数据库访问帮助器。</summary>
    public PatientController(DbHelper dbHelper, IPatientDetailsExportService exportService)
    {
        _dbHelper = dbHelper;
        _exportService = exportService;
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
             SELECT dailyno, patientnameen, patientnamechi, productcode, "repeat", refcustordr,
                 inputtype_a, inputtype_b, status, all_fields_equal, differing_fields,
                 formtype_a, formtype_b
            FROM public.get_patientdetails_field_differences()
            ORDER BY dailyno DESC
            """,
            reader => new PatientDetailsFieldDifference(
                reader.GetString(0),
              reader.IsDBNull(1) ? null : reader.GetString(1),
              reader.IsDBNull(2) ? null : reader.GetString(2),
              reader.IsDBNull(3) ? null : reader.GetFieldValue<string[]>(3),
                            reader.IsDBNull(4) ? null : reader.GetInt32(4),
                            reader.IsDBNull(5) ? null : reader.GetString(5),
                                reader.GetInt32(6),
                            reader.GetInt32(7),
                            reader.GetString(8),
                            reader.GetBoolean(9),
                            reader.IsDBNull(10) ? null : reader.GetFieldValue<string[]>(10),
                            reader.IsDBNull(11) ? null : reader.GetString(11),
                            reader.IsDBNull(12) ? null : reader.GetString(12)
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
        var statusValues = PatientDetailsExportService.NormalizeStatuses(request.Statuses);

        if (statusValues.Length == 0)
        {
            return BadRequest(new { message = "status 不能为空" });
        }

        var exportFormat = request.Format?.Trim().ToLowerInvariant() ?? "excel";
        if (exportFormat is not ("excel" or "csv"))
        {
            return BadRequest(new { message = "format 只支持 excel 或 csv" });
        }

        var rows = await _exportService.GetRowsAsync(statusValues, cancellationToken);

        if (rows.Count == 0)
        {
            return NotFound(new { message = "没有符合状态的最终患者资料" });
        }

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
        var excelHeaders = new[]
        {
            "Lab No.",
            "Daily No.",
            "English Name",
            "Chinese Name",
            "Product Code",
            "Date of Birth",
            "Maternal Age",
            "Gestational Week",
            "Pregnancy",
            "Ethnic Origin",
            "ID/Passport No.",
            "Clinic Code",
            "Sample Type",
            "Patient Ref No.",
            "Date Received",
            "Date Collected",
            "TBC Status",
            "Hold Status",
            "History of Abortion",
            "IVF",
            "Remarks"
        };

        for (var columnIndex = 0; columnIndex < excelHeaders.Length; columnIndex++)
        {
            worksheet.Cell(1, columnIndex + 1).Value = excelHeaders[columnIndex];
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < excelHeaders.Length; columnIndex++)
            {
                var value = rows[rowIndex].GetValueOrDefault(excelHeaders[columnIndex]);
                worksheet.Cell(rowIndex + 2, columnIndex + 1).Value =
                    XLCellValue.FromObject(value, CultureInfo.InvariantCulture);
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

    /// <summary>按每日编号获取最终患者的基本资料。</summary>
    [HttpGet("getPatientdetailsFinalByDailyno")]
    public async Task<IActionResult> GetPatientDetailsFinalByDailyNo(
        [FromQuery] string dailyNo,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dailyNo))
        {
            return BadRequest(new { message = "dailyNo 不能为空" });
        }

        var patients = await _dbHelper.QueryAsync(
            """
            SELECT
                dailyno,
                dateofbirth::date AS dateofbirth,
                COALESCE(NULLIF(btrim(patientnamechi), ''), patientnameen) AS patientname,
                gender,
                hkidno
            FROM public.patientdetails_final
            WHERE dailyno = @dailyno
            """,
            reader => new
            {
                dailyno = reader.GetString(0),
                dateOfBirth = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1),
                patientName = reader.IsDBNull(2) ? null : reader.GetString(2),
                gender = reader.IsDBNull(3) ? null : reader.GetString(3),
                hkidno = reader.IsDBNull(4) ? null : reader.GetString(4)
            },
            [Parameter("dailyno", dailyNo.Trim())],
            cancellationToken);

        if (patients.Count == 0)
        {
            return NotFound(new { message = "找不到对应的最终患者资料" });
        }

        if (patients.Count > 1)
        {
            return Conflict(new { message = "找到多笔最终患者资料，请提供更多筛选条件" });
        }

        return Ok(patients[0]);
    }

    /// <summary>按每日编号获取最终患者资料的全部字段，保留原基本资料查询接口。</summary>
    [HttpGet("getPatientdetailsFinalFullByDailyno")]
    [EndpointSummary("Get all final patient fields by dailyNo")]
    [ProducesResponseType(typeof(JsonElement), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetPatientDetailsFinalFullByDailyNo(
        [FromQuery, Required, StringLength(255)] string dailyNo,
        [FromServices] IPatientDetailsFinalQueryService service,
        [FromServices] ILogger<PatientController> logger,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        if (string.IsNullOrWhiteSpace(dailyNo) || dailyNo.Length > 255)
        {
            ModelState.AddModelError("dailyNo", "dailyNo 不能为空且不能超过 255 个字符。");
            return ValidationProblem(ModelState);
        }

        try
        {
            var patients = await service.GetByDailyNo(dailyNo.Trim(), cancellationToken);
            return patients.Count switch
            {
                0 => Problem(statusCode: StatusCodes.Status404NotFound, title: "找不到对应的最终患者资料。"),
                1 => Ok(patients[0]),
                _ => Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "找到多笔最终患者资料，无法通过 dailyNo 唯一确定患者。")
            };
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to retrieve full final patient details.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "获取最终患者资料失败。");
        }
    }

    /// <summary>按每日编号获取上一笔或下一笔患者资料。</summary>
    [HttpPost("getAdjacentPatientdetails")]
    public async Task<IActionResult> GetAdjacentPatientDetails(
        [FromBody] GetAdjacentPatientDetailsRequest request,
        CancellationToken cancellationToken)
    {
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
                    p_inputtype => @inputtype::integer
                )
                """,
                MapCurrentRow,
                [
                    Parameter("dailyno", request.DailyNo?.Trim()),
                    Parameter("direction", direction),
                    Parameter("formtype", request.FormType),
                    Parameter("inputtype", request.InputType)
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

    /// <summary>按表单类型和日期获取每日编号缺口范围。</summary>
    [HttpPost("getPatientdetailsDailynoGaps")]
    public async Task<IActionResult> GetPatientDetailsDailyNoGaps(
        [FromBody] GetPatientDetailsDailyNoGapsRequest request,
        CancellationToken cancellationToken)
    {
        var requestedFormTypes = request.FormTypes;
        if (requestedFormTypes is null
            || requestedFormTypes.Count == 0
            || requestedFormTypes.Any(formType =>
                string.IsNullOrWhiteSpace(formType) || formType.Trim().Length > 2))
        {
            return BadRequest(new { message = "formtypes 至少需要一个值，且每个 formtype 长度不能超过 2 个字符" });
        }

        if (request.SequenceDate is null)
        {
            return BadRequest(new { message = "sequence_date 不能为空" });
        }

        var formTypes = requestedFormTypes
            .Select(formType => formType!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var results = await _dbHelper.QueryAsync(
            """
            WITH requested_formtypes AS
            (
                SELECT DISTINCT requested.formtype
                FROM unnest(@formtypes::varchar[]) AS requested(formtype)
            ),
            ranges AS
            (
                SELECT DISTINCT gaps.formtype, range_items.range_value
                FROM public.patientdetails_dailyno_gaps AS gaps
                CROSS JOIN LATERAL jsonb_array_elements(gaps.missing_ranges) AS range_items(range_value)
                WHERE gaps.formtype = ANY(@formtypes::varchar[])
                  AND gaps.sequence_date = @sequence_date
            )
            SELECT
                requested_formtypes.formtype,
                COALESCE(
                    jsonb_agg(ranges.range_value ORDER BY ranges.range_value::text)
                        FILTER (WHERE ranges.range_value IS NOT NULL),
                    '[]'::jsonb
                )::text AS gaplist
            FROM requested_formtypes
            LEFT JOIN ranges ON ranges.formtype = requested_formtypes.formtype
            GROUP BY requested_formtypes.formtype
            ORDER BY requested_formtypes.formtype
            """,
            reader => new
            {
                FormType = reader.GetString(0),
                GapList = reader.GetString(1)
            },
            [
                Parameter("formtypes", formTypes),
                Parameter("sequence_date", request.SequenceDate.Value)
            ],
            cancellationToken);

        return Ok(new
        {
            sequenceDate = request.SequenceDate.Value,
            gapLists = results.Select(result => new
            {
                formType = result.FormType,
                gapList = JsonDocument.Parse(result.GapList).RootElement.Clone()
            })
        });
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
        var deleteCounts = await _dbHelper.QueryAsync(
            """
            WITH deleted_patientdetails AS
            (
                DELETE FROM public.patientdetails
                WHERE dailyno = @dailyno
                  AND inputtype = @inputtype
                RETURNING dailyno
            ),
            deleted_daily_nos AS
            (
                SELECT dailyno, count(*) AS deleted_count
                FROM deleted_patientdetails
                GROUP BY dailyno
            ),
            deleted_patientdetails_final AS
            (
                DELETE FROM public.patientdetails_final AS final
                USING deleted_daily_nos AS deleted
                WHERE final.dailyno = deleted.dailyno
                  AND deleted.deleted_count =
                      (SELECT count(*) FROM public.patientdetails AS remaining
                       WHERE remaining.dailyno = deleted.dailyno)
                RETURNING 1
            )
            SELECT
                (SELECT count(*) FROM deleted_patientdetails) AS deleted_count,
                (SELECT count(*) FROM deleted_patientdetails_final) AS deleted_final_count
            """,
            reader => new
            {
                DeletedCount = reader.GetInt64(0),
                DeletedFinalCount = reader.GetInt64(1)
            },
            [
                Parameter("dailyno", dailyNo),
                Parameter("inputtype", request.InputType)
            ],
            cancellationToken);

        var deleteCount = deleteCounts[0];
        if (deleteCount.DeletedCount == 0)
        {
            return NotFound(new { message = "找不到对应的患者资料" });
        }

        return Ok(new
        {
            message = "患者资料已删除",
            dailyNo,
            inputType = request.InputType,
            deletedCount = deleteCount.DeletedCount,
            deletedFinalCount = deleteCount.DeletedFinalCount
        });
    }

    /// <summary>按每日编号及其对应输入类型批量删除患者资料。</summary>
    [HttpPost("deletePatientdetailsByDailynos")]
    public async Task<IActionResult> DeletePatientDetailsByDailyNos(
        [FromBody] DeletePatientDetailsByDailyNosRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Length == 0)
        {
            return BadRequest(new { message = "items 不能为空" });
        }

        if (request.Items.Any(item => item is null || string.IsNullOrWhiteSpace(item.DailyNo)))
        {
            return BadRequest(new { message = "items 中的 dailyNo 不能为空" });
        }

        if (request.Items.Any(item => item!.InputType is null))
        {
            return BadRequest(new { message = "items 中的 inputType 不能为空" });
        }

        var items = request.Items
            .Select(item => (DailyNo: item!.DailyNo!.Trim(), InputType: item.InputType!.Value))
            .Distinct()
            .ToArray();
        var parameters = new List<NpgsqlParameter>();
        var pairPlaceholders = string.Join(", ", items.Select((item, index) =>
        {
            parameters.Add(Parameter($"dailyno{index}", item.DailyNo));
            parameters.Add(Parameter($"inputtype{index}", item.InputType));
            return $"(@dailyno{index}, @inputtype{index})";
        }));

        var deletedCount = await _dbHelper.ExecuteNonQueryAsync(
            $"DELETE FROM public.patientdetails WHERE (dailyno, inputtype) IN ({pairPlaceholders})",
            parameters,
            cancellationToken);

        if (deletedCount == 0)
        {
            return NotFound(new { message = "找不到对应的患者资料" });
        }

        return Ok(new
        {
            message = "患者资料已批量删除",
            items,
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

    /// <summary>部分更新最终患者资料，并将完整结果同步至两条录入记录。</summary>
    [HttpPost("updatePatientdetailsFinalByDailyno")]
    [ServiceFilter(typeof(SyncApiActionContext))]
    [EndpointSummary("Update final patient and input records, then update previously synchronized systems selected by formtype rules")]
    [ProducesResponseType(typeof(JsonElement), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> UpdatePatientDetailsFinalByDailyNo(
        [FromBody] UpdatePatientDetailsFinalRequest request,
        [FromServices] IPatientDetailsFinalUpdateService service,
        [FromServices] IPatientSyncRoutingService routingService,
        [FromServices] ILogger<PatientController> logger,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid User ID" });
        }

        try
        {
            var updatedPatient = await service.Update(request, userId, cancellationToken);
            var response = JsonNode.Parse(updatedPatient.GetRawText()) as JsonObject
                ?? throw new InvalidOperationException("Final patient update returned a non-object JSON result.");
            var centreId = response["centreid"]?.GetValue<string>() ?? string.Empty;
            var syncResults = await routingService.UpdateAlreadySynced(
                new SyncRoutingPatient(centreId, request.FormType.Trim(), request.DailyNo.Trim()), cancellationToken);
            var lis = syncResults.FirstOrDefault(result => result.TargetSystem == "LIS");
            var lisResult = new LisFinalPatientUpdateResult(
                lis is not null && lis.Operation != "Skipped",
                lis is null || lis.Operation == "Skipped" ? "skipped" : lis.Success ? "succeeded" : "unknown",
                lis?.Stage, lis?.HttpStatus, lis?.Message);
            if (syncResults.Any(result => !result.Success))
            {
                var status = syncResults.Any(result => !result.Success && result.HttpStatus == 500)
                    ? StatusCodes.Status500InternalServerError : StatusCodes.Status502BadGateway;
                var problem = new ProblemDetails
                {
                    Status = status,
                    Title = "本地患者资料已更新，但部分外部系统更新未完成。",
                    Detail = "请检查 syncResults 中各系统的更新结果。"
                };
                problem.Extensions["localDatabaseUpdated"] = true;
                problem.Extensions["syncResults"] = syncResults;
                problem.Extensions["lisSyncStatus"] = lisResult.Status;
                problem.Extensions["lisSyncStage"] = lisResult.Stage;
                problem.Extensions["lisHttpStatus"] = lisResult.HttpStatus;
                return StatusCode(status, problem);
            }

            response["lisSync"] = JsonSerializer.SerializeToNode(lisResult);
            response["syncResults"] = JsonSerializer.SerializeToNode(syncResults, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Ok(response);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.NoDataFound)
        {
            logger.LogWarning("Final patient update found no matching final record.");
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "找不到对应的最终患者资料。");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.TooManyRows
            || (exception.SqlState == PostgresErrorCodes.RaiseException
                && (exception.MessageText.StartsWith("Expected exactly one patientdetails row for each inputtype 1 and 2", StringComparison.Ordinal)
                    || exception.MessageText.StartsWith("Expected to update exactly two patientdetails rows", StringComparison.Ordinal))))
        {
            logger.LogWarning("Final patient update was blocked by missing or duplicate patient records.");
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "最终患者记录必须唯一，且 inputtype 1、2 的录入记录必须各有一条。未完成更新。");
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to update final patient details.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "更新最终患者资料失败。");
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
            bool boolean => boolean ? "TRUE" : "FALSE",
            Array values => string.Join(", ", values.Cast<object?>().Select(FormatExportValue)),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private sealed record OptionItem(int Id, string Description);

}
