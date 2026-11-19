using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace medxbackend.Models;

/// <summary>Identifies the existing LIS order to update from current local patient data.</summary>
public sealed record LisUpdateOrderFromPatientRequest
{
    [Required, StringLength(255)]
    public string CentreId { get; init; } = string.Empty;
}

/// <summary>Order fields sent to the LIS UpdateOrder endpoint.</summary>
public sealed record LisUpdateOrderRequest
{
    [Required]
    public required string LabId { get; init; }

    [Required]
    public required string ModifiedBy { get; init; }

    [Required]
    public required string SiteCode { get; init; }

    [Required]
    public required string ReferringDoctorCode { get; init; }

    [Required]
    public required string BillingDoctorCode { get; init; }
}

/// <summary>Patient fields nested in the LIS UpdateOrderPatient request.</summary>
public sealed record LisUpdateOrderPatientDetails
{
    [JsonPropertyName("HKID")]
    public string? HKID { get; init; }

    [JsonPropertyName("patientNameEng")]
    public string? PatientNameEng { get; init; }

    [JsonPropertyName("patientNameChi")]
    public string? PatientNameChi { get; init; }

    [JsonPropertyName("sex")]
    [Required, RegularExpression("^(F|M|U)$")]
    public required string Sex { get; init; }

    [JsonPropertyName("ageType")]
    [Required, RegularExpression("^(DOB|AGE)$")]
    public string AgeType { get; init; } = "AGE";

    [JsonPropertyName("dob")]
    public DateTimeOffset? DOB { get; init; }

    [JsonPropertyName("age")]
    [Required, Range(0, int.MaxValue)]
    public int? Age { get; init; }

    [JsonPropertyName("ageUnit")]
    [Required, RegularExpression("^Y$")]
    public string AgeUnit { get; init; } = "Y";
}

/// <summary>Request sent to the LIS UpdateOrderPatient endpoint.</summary>
public sealed record LisUpdateOrderPatientRequest
{
    [Required]
    public required string LabId { get; init; }

    [Required]
    public required string ModifiedBy { get; init; }

    [JsonPropertyName("patient")]
    [Required]
    public required LisUpdateOrderPatientDetails Patient { get; init; }
}
