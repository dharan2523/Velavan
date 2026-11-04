namespace VelavanDemo.Application.Security;

public static class PasswordPolicy
{
    public const int MinimumLength = 10;

    public static IReadOnlyList<string> Validate(string? password)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength)
        {
            errors.Add($"Password must be at least {MinimumLength} characters long.");
        }

        if (password is null || !password.Any(char.IsUpper))
        {
            errors.Add("Password must contain at least one uppercase letter.");
        }

        if (password is null || !password.Any(char.IsLower))
        {
            errors.Add("Password must contain at least one lowercase letter.");
        }

        if (password is null || !password.Any(char.IsDigit))
        {
            errors.Add("Password must contain at least one digit.");
        }

        if (password is null || !password.Any(character => !char.IsLetterOrDigit(character)))
        {
            errors.Add("Password must contain at least one special character.");
        }

        return errors;
    }
}
