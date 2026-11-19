using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace medxbackend.Models;

/// <summary>Common LIS order parameters shared by single and batch requests.</summary>
public abstract record LisOrderParameters : IValidatableObject
{
    protected virtual bool ValidateDoctorCodes => true;

    [Required, RegularExpression("^(R|S)$")]
    public string Priority { get; init; } = "R";

    [JsonPropertyName("referringDoctorCode")]
    public string? ReferringDoctorCode { get; init; }
    public string? BillingDoctorCode { get; init; }
    public string? CCDoctorCode { get; init; }
    public string? ReferringCompanyCode { get; init; }
    public string? BillingCompanyCode { get; init; }
    public string? RefNumber { get; init; }

    [Required]
    public string SiteCode { get; init; } = string.Empty;

    public bool FOC { get; init; }

    [RegularExpression("^(A|C)$")]
    public string? PaymentMethod { get; init; }

    [Required, RegularExpression("^(A|C)$")]
    public string PaymentOption { get; init; } = "A";

    public decimal? CashAmount { get; init; }
    public bool? ReportByPrint { get; init; }
    public bool? ReportByFax { get; init; }
    public string? Fax { get; init; }
    public bool OverrideFaxInd { get; init; } = true;
    public bool? ReportByEmail { get; init; }
    public string? Email { get; init; }
    public bool OverrideEmailInd { get; init; } = true;
    public bool? ReportBySMS { get; init; }
    public string? SMS { get; init; }
    public bool OverrideSMSInd { get; init; } = true;
    public bool? ReportByWhatsapp { get; init; }
    public string? Whatsapp { get; init; }
    public bool OverrideWhatsappInd { get; init; } = true;
    public bool? ReportByPhone { get; init; }
    public bool? ReportByInterface { get; init; }
    public bool? ReportByEHR { get; init; }
    public string? InterfaceProfile { get; init; }
    public string? SID { get; init; }
    public string? VisitCode { get; init; }
    public string? ProjectCode { get; init; }
    public string? RegionalLab { get; init; }
    public string? ChitNumber { get; init; }
    public string? ContractNumber { get; init; }
    public string? MedicalCardNumber { get; init; }
    public bool? ShowCumulativeResults { get; init; }
    public string? ReportComment { get; init; }
    public string? InternalComment { get; init; }

    public IReadOnlyList<LisOrderTestProfileRequest>? TestProfiles { get; init; }
    public IReadOnlyList<LisOrderSampleRequest>? Samples { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ValidateDoctorCodes && string.IsNullOrWhiteSpace(ReferringDoctorCode) && string.IsNullOrWhiteSpace(ReferringCompanyCode))
        {
            yield return new ValidationResult("A referring doctor or company is required.",
                [nameof(ReferringDoctorCode), nameof(ReferringCompanyCode)]);
        }

        if (ValidateDoctorCodes && string.IsNullOrWhiteSpace(BillingDoctorCode) && string.IsNullOrWhiteSpace(BillingCompanyCode))
        {
            yield return new ValidationResult("A billing doctor or company is required.",
                [nameof(BillingDoctorCode), nameof(BillingCompanyCode)]);
        }

        if ((string.IsNullOrWhiteSpace(PaymentMethod) ? PaymentOption : PaymentMethod) == "C" && CashAmount is null)
        {
            yield return new ValidationResult("CashAmount is required when paying by cash.", [nameof(CashAmount)]);
        }

        if (ReportByInterface == true && string.IsNullOrWhiteSpace(InterfaceProfile))
        {
            yield return new ValidationResult("InterfaceProfile is required when reporting by interface.",
                [nameof(InterfaceProfile)]);
        }

        if (TestProfiles?.Any(profile => profile is null) == true)
        {
            yield return new ValidationResult("TestProfiles cannot contain null entries.", [nameof(TestProfiles)]);
        }

        if (Samples?.Any(sample => sample is null) == true)
        {
            yield return new ValidationResult("Samples cannot contain null entries.", [nameof(Samples)]);
        }
    }
}