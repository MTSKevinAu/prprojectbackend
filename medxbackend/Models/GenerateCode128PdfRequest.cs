using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

/// <summary>生成包含 Code 128 条码和病人信息的 PDF。</summary>
public sealed record GenerateCode128PdfRequest
{
    /// <summary>要输出到 PDF 的每日编号列表。</summary>
    [Required, MinLength(1), MaxLength(60)]
    public List<string>? DailyNos { get; init; }
}