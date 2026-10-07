using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

/// <summary>按一个或多个状态导出最终患者资料的请求。</summary>
public sealed record ExportPatientDetailsFinalRequest
{
    [Required]
    [MinLength(1)]
    public string[]? Statuses { get; init; }

    public string? Format { get; init; } = "excel";
}