using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

public sealed class CreatePatientDetailRequest
{
    public string? CentreId { get; set; }
    public string? DailyNo { get; set; }
    public string? PatientNameEn { get; set; }
    public string? PatientNameChi { get; set; }
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public int? Age { get; set; }
    public string? HkidNo { get; set; }
    public string? PassportNo { get; set; }
    public string? Mobile { get; set; }
    public string? RefCustOrDr { get; set; }
    public string? PatientRefNo { get; set; }
    public string? BillToCustOrDr { get; set; }
    public string? Speciment { get; set; }
    public string? Remarks { get; set; }
    public DateTime? DateReceived { get; set; }
    public DateTime? DateCollected { get; set; }
    public int? GestationalWeek { get; set; }
    public int? GestationalDay { get; set; }
    public string? Pregnancy { get; set; }
    public string? EthnicOrigin { get; set; }
    public string? FormType { get; set; }
    public int? InputType { get; set; }
    public string? StartNo { get; set; }
    public string[]? ProductCode { get; set; }
    public int? Ivf { get; set; }
    public string? Redo { get; set; }
    public int? HxOfAbortion { get; set; }
    public int? Hold { get; set; }
    public int? Tbc { get; set; }
}
