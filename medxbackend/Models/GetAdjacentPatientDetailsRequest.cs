using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

/// <summary>查询指定每日编号之前或之后患者资料的请求。</summary>
public sealed class GetAdjacentPatientDetailsRequest
{
    [Required]
    [StringLength(255)]
    public string? DailyNo { get; init; }

    [Required]
    public string? Direction { get; init; }

    [StringLength(2)]
    public string? FormType { get; init; }

    public int? InputType { get; init; }
}