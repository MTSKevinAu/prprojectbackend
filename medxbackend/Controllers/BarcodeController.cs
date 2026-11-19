using medxbackend.Data;
using medxbackend.Models;
using medxbackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Globalization;
using System.IO;
using System.Net.Sockets;

namespace medxbackend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class BarcodeController : ControllerBase
{
    private readonly DbHelper _dbHelper;
    private readonly IBarcodeService _barcodeService;
    private readonly IReceiptPrinterService _receiptPrinterService;
    private readonly ILogger<BarcodeController> _logger;
    private readonly bool _configuredCode39;

    public BarcodeController(
        DbHelper dbHelper,
        IBarcodeService barcodeService,
        IReceiptPrinterService receiptPrinterService,
        ILogger<BarcodeController> logger,
        IOptions<BarcodeOptions> barcodeOptions)
    {
        _dbHelper = dbHelper;
        _barcodeService = barcodeService;
        _receiptPrinterService = receiptPrinterService;
        _logger = logger;
        _configuredCode39 = string.Equals(
            barcodeOptions.Value.Format,
            "Code39",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>根据 Barcode:Format 配置生成病人条码 PDF。</summary>
    [HttpPost("pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> GenerateConfiguredPdf(
        [FromBody] GenerateCode128PdfRequest request,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolvePatientsAsync(request.DailyNos, cancellationToken);
        if (resolution.Error is not null)
        {
            return resolution.Error;
        }

        var validationError = ValidatePatients(resolution.Patients, _configuredCode39);
        if (validationError is not null)
        {
            return validationError;
        }

        var formatName = _configuredCode39 ? "code39" : "code128";
        return File(
            CreatePatientPdfBytes(resolution.Patients!, _configuredCode39),
            "application/pdf",
            $"{formatName}-patients.pdf");
    }

    /// <summary>根据 Barcode:Format 配置生成标签并发送到网络打印机。</summary>
    [HttpPost("pdf/print")]
    [ProducesResponseType(typeof(PrintCode128PdfResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PrintCode128PdfResponse>> PrintConfiguredPdf(
        [FromBody] GenerateCode128PdfRequest request,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolvePatientsAsync(request.DailyNos, cancellationToken);
        if (resolution.Error is not null)
        {
            return resolution.Error;
        }

        var validationError = ValidatePatients(resolution.Patients, _configuredCode39);
        if (validationError is not null)
        {
            return validationError;
        }

        try
        {
            var pdf = CreatePatientPdfBytes(resolution.Patients!, _configuredCode39);
            var pagesPrinted = await _receiptPrinterService.PrintPdfAsync(pdf);
            return Ok(new PrintCode128PdfResponse(pagesPrinted));
        }
        catch (PrinterNotConfiguredException)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "网络打印机尚未配置");
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            _logger.LogError(exception, "Failed to print configured barcode PDF to the network printer.");
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "无法连接网络打印机");
        }
    }

    private static BadRequestObjectResult? ValidatePatients(
        IReadOnlyList<BarcodePatientLabel>? patients,
        bool useCode39)
    {
        if (patients is null || patients.Count is < 1 or > 60)
        {
            return new BadRequestObjectResult(new { message = "patients 必须包含 1 到 60 位病人" });
        }

        var hasInvalidPatient = patients.Any(patient => patient is null
            || patient.DateOfBirth is null
            || string.IsNullOrWhiteSpace(patient.PatientName)
            || patient.Gender is not ("M" or "F")
            || patient.Age is null or < 0 or > 150);
        if (hasInvalidPatient)
        {
            return new BadRequestObjectResult(new { message = "病人资料不完整或格式无效" });
        }

        var hasInvalidBarcode = useCode39
            ? patients.Any(patient => patient is null || !IsValidCode39Value(patient.Barcode))
            : patients.Any(patient => patient is null || string.IsNullOrWhiteSpace(patient.Barcode));

        return hasInvalidBarcode
            ? new BadRequestObjectResult(new
            {
                message = useCode39
                    ? "Code 39 条码只能包含大写字母、数字和空格 $ % + - . /"
                    : "每位病人的 barcode 不能为空"
            })
            : null;
    }

    private async Task<(List<BarcodePatientLabel>? Patients, ActionResult? Error)> ResolvePatientsAsync(
        IReadOnlyList<string>? dailyNoValues,
        CancellationToken cancellationToken)
    {
        if (dailyNoValues is null || dailyNoValues.Count is < 1 or > 60)
        {
            return (null, BadRequest(new { message = "dailyNos 必须包含 1 到 60 个每日编号" }));
        }

        if (dailyNoValues.Any(string.IsNullOrWhiteSpace))
        {
            return (null, BadRequest(new { message = "dailyNos 中的每日编号不能为空" }));
        }

        var normalizedDailyNos = dailyNoValues
            .Select(dailyNo => dailyNo.Trim())
            .ToArray();
        var uniqueDailyNos = normalizedDailyNos
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var records = await _dbHelper.QueryAsync(
            """
            SELECT
                dailyno,
                dateofbirth::date AS dateofbirth,
                COALESCE(NULLIF(btrim(patientnamechi), ''), patientnameen) AS patientname,
                gender,
                hkidno
            FROM public.patientdetails_final
            WHERE dailyno = ANY(@dailynos::varchar[])
            """,
            reader => new PatientBarcodeDetails(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)),
            [new NpgsqlParameter("dailynos", uniqueDailyNos)],
            cancellationToken);

        var recordsByDailyNo = records
            .GroupBy(record => record.DailyNo, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var resolvedPatients = new List<BarcodePatientLabel>(normalizedDailyNos.Length);

        foreach (var dailyNo in normalizedDailyNos)
        {
            if (!recordsByDailyNo.TryGetValue(dailyNo, out var matches))
            {
                return (null, NotFound(new { message = $"找不到每日编号 {dailyNo} 的最终患者资料" }));
            }

            if (matches.Length > 1)
            {
                return (null, Conflict(new { message = $"每日编号 {dailyNo} 对应多笔最终患者资料" }));
            }

            var record = matches[0];
            resolvedPatients.Add(new BarcodePatientLabel(
                dailyNo,
                dailyNo,
                record.DateOfBirth,
                record.PatientName,
                record.Gender,
                record.DateOfBirth is null ? null : CalculateAge(record.DateOfBirth.Value),
                record.HkidNo));
        }

        return (resolvedPatients, null);
    }

    private static int CalculateAge(DateOnly dateOfBirth)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var age = today.Year - dateOfBirth.Year;
        return dateOfBirth > today.AddYears(-age) ? age - 1 : age;
    }

    private byte[] CreatePatientPdfBytes(
        IReadOnlyList<BarcodePatientLabel> patients,
        bool useCode39 = false)
    {
        using var document = new PdfDocument();
        document.Info.Title = useCode39 ? "Code 39 Patient Labels" : "Code 128 Patient Labels";

        const int productsPerPage = 1;
        const double pageWidthMillimeters = 55;
        const double pageHeightMillimeters = 25;
        const double pageMarginMillimeters = 2;
        var pageWidth = MillimetersToPoints(pageWidthMillimeters);
        var margin = MillimetersToPoints(pageMarginMillimeters);
        var contentWidth = pageWidth - margin * 2;
        var bodyFont = new XFont("Noto Sans", 7, XFontStyleEx.Regular);
        var chineseBodyFont = new XFont("Noto Sans SC", 7, XFontStyleEx.Regular);

        for (var startIndex = 0; startIndex < patients.Count; startIndex += productsPerPage)
        {
            var patientsOnPage = Math.Min(productsPerPage, patients.Count - startIndex);
            var page = document.AddPage();
            page.Width = XUnit.FromMillimeter(pageWidthMillimeters);
            page.Height = XUnit.FromMillimeter(pageHeightMillimeters);
            using var graphics = XGraphics.FromPdfPage(page);

            for (var index = startIndex; index < Math.Min(startIndex + productsPerPage, patients.Count); index++)
            {
                var patient = patients[index];

                var barcode = useCode39
                    ? _barcodeService.GenerateCode39Png(patient.Barcode!.Replace("-", string.Empty, StringComparison.Ordinal), 600, 120)
                    : _barcodeService.GenerateCode128Png(patient.Barcode!, 600, 120);
                using var barcodeStream = new MemoryStream(barcode);
                using var barcodeImage = XImage.FromStream(barcodeStream);
                var barcodeHeight = contentWidth * 120 / 600;
                graphics.DrawImage(barcodeImage, margin, MillimetersToPoints(0.5), contentWidth, barcodeHeight);

                var firstLineX = margin;
                var firstLineWidth = contentWidth;
                var firstLineY = MillimetersToPoints(11.5);
                var firstLineHeight = MillimetersToPoints(3.5);
                graphics.DrawString(patient.RecordNo ?? string.Empty, bodyFont, XBrushes.Black,
                    new XRect(firstLineX, firstLineY, firstLineWidth / 2, firstLineHeight), XStringFormats.TopLeft);
                graphics.DrawString($"DOB:{patient.DateOfBirth!.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}", bodyFont, XBrushes.Black,
                    new XRect(firstLineX + firstLineWidth / 2, firstLineY, firstLineWidth / 2, firstLineHeight), XStringFormats.TopRight);
                graphics.DrawString($"{patient.PatientName}    {patient.Gender}/{patient.Age}Y", chineseBodyFont, XBrushes.Black,
                    new XRect(margin, MillimetersToPoints(15.5), contentWidth, MillimetersToPoints(3.5)), XStringFormats.TopLeft);
                graphics.DrawString($"ID:{patient.PatientId}", bodyFont, XBrushes.Black,
                    new XRect(margin, MillimetersToPoints(19.5), contentWidth, MillimetersToPoints(3.5)), XStringFormats.TopLeft);
            }
        }

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private static bool IsValidCode39Value(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(character => char.IsAsciiLetterUpper(character)
            || char.IsAsciiDigit(character)
            || " $%+-./".Contains(character));

    private static double MillimetersToPoints(double millimeters) => XUnit.FromMillimeter(millimeters).Point;

    private sealed record PatientBarcodeDetails(
        string DailyNo,
        DateOnly? DateOfBirth,
        string? PatientName,
        string? Gender,
        string? HkidNo);

    private sealed record BarcodePatientLabel(
        string? Barcode,
        string? RecordNo,
        DateOnly? DateOfBirth,
        string? PatientName,
        string? Gender,
        int? Age,
        string? PatientId);
}