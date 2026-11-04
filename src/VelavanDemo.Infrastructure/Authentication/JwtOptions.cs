using System.Text;

namespace VelavanDemo.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
    public string Secret { get; set; } = string.Empty;

    public static void Validate(JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            throw new InvalidOperationException("JWT issuer is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException("JWT audience is not configured.");
        }

        if (options.AccessTokenMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("JWT access token minutes must be between 1 and 1440.");
        }

        if (Encoding.UTF8.GetByteCount(options.Secret) < 32)
        {
            throw new InvalidOperationException(
                "JWT secret must be at least 32 bytes. Configure Jwt:Secret through user-secrets or environment variables.");
        }
    }
}
