using System.ComponentModel.DataAnnotations;
using VelavanDemo.Application.Security;

namespace VelavanDemo.Application.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class StrongPasswordAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        var errors = PasswordPolicy.Validate(value.ToString());

        return errors.Count == 0
            ? ValidationResult.Success
            : new ValidationResult(string.Join(" ", errors));
    }
}
