using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace medxbackend.Models;

/// <summary>Patient identity and age details supplied with an LIS order.</summary>
public sealed record LisOrderPatientRequest : IValidatableObject
{
    public string? HKID { get; init; }
    public string? ID { get; init; }

    [JsonPropertyName("patientNameEng")]
    public string? PatientNameEng { get; init; }

    [JsonPropertyName("patientNameChi")]
    public string? PatientNameChi { get; init; }

    [JsonPropertyName("sex")]
    [Required, RegularExpression("^(F|M|U)$")]
    public string Sex { get; init; } = string.Empty;

    [JsonPropertyName("ageType")]
    [Required, RegularExpression("^(DOB|AGE|ADULT|CHILD)$")]
    public string AgeType { get; init; } = string.Empty;

    [JsonPropertyName("dob")]
    public DateTimeOffset? DOB { get; init; }

    [JsonPropertyName("age")]
    [Range(0, int.MaxValue)]
    public int? Age { get; init; }

    [JsonPropertyName("ageUnit")]
    [RegularExpression("^(Y|M|D|W)$")]
    public string? AgeUnit { get; init; } = "Y";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(PatientNameEng) && string.IsNullOrWhiteSpace(PatientNameChi))
        {
            yield return new ValidationResult("An English or Chinese patient name is required.",
                [nameof(PatientNameEng), nameof(PatientNameChi)]);
        }

        if (AgeType == "DOB" && DOB is null)
        {
            yield return new ValidationResult("DOB is required when AgeType is DOB.", [nameof(DOB)]);
        }

        if (AgeType == "AGE")
        {
            if (Age is null)
            {
                yield return new ValidationResult("Age is required when AgeType is AGE.", [nameof(Age)]);
            }

            if (string.IsNullOrWhiteSpace(AgeUnit))
            {
                yield return new ValidationResult("AgeUnit is required when AgeType is AGE.", [nameof(AgeUnit)]);
            }
        }
    }
}