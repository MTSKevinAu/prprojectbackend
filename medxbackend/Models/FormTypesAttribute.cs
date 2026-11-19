using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

public sealed class FormTypesAttribute : ValidationAttribute
{
    public FormTypesAttribute() : base("FormTypes must contain at least one non-empty form type of at most 2 characters.")
    {
    }

    public override bool IsValid(object? value)
        => value is string[] { Length: > 0 } formTypes
            && formTypes.All(formType => !string.IsNullOrWhiteSpace(formType) && formType.Trim().Length <= 2);
}
