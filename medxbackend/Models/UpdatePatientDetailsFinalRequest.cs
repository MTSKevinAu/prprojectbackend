using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

/// <summary>Partially updates final patient data and synchronizes both input records; omitted or null fields retain their values.</summary>
public sealed record UpdatePatientDetailsFinalRequest
{
    [Required, StringLength(255)]
    public required string DailyNo { get; init; }

    [Required, StringLength(2)]
    public required string FormType { get; init; }

    [StringLength(255)] public string? PatientNameEn { get; init; }
    [StringLength(255)] public string? PatientNameChi { get; init; }
    [StringLength(255)] public string? Gender { get; init; }
    public DateTimeOffset? DateOfBirth { get; init; }
    public int? Age { get; init; }
    [StringLength(255)] public string? HkidNo { get; init; }
    [StringLength(255)] public string? PassportNo { get; init; }
    [StringLength(255)] public string? Mobile { get; init; }
    [StringLength(255)] public string? RefCustOrDr { get; init; }
    [StringLength(255)] public string? PatientRefNo { get; init; }
    [StringLength(255)] public string? BillToCustOrDr { get; init; }
    [StringLength(255)] public string? Speciment { get; init; }
    [StringLength(255)] public string? Remarks { get; init; }
    public DateTimeOffset? DateReceived { get; init; }
    public DateTimeOffset? DateCollected { get; init; }
    public int? GestationalWeek { get; init; }
    public int? GestationalDay { get; init; }
    [StringLength(255)] public string? Pregnancy { get; init; }
    [StringLength(255)] public string? EthnicOrigin { get; init; }
    public string[]? ProductCode { get; init; }
    public int? Ivf { get; init; }
    public string? Redo { get; init; }
    public int? HxOfAbortion { get; init; }
    public int? Hold { get; init; }
    public int? Tbc { get; init; }
}
