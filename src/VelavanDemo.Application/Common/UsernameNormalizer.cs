namespace VelavanDemo.Application.Common;

public static class UsernameNormalizer
{
    public static string Normalize(string? username)
    {
        return (username ?? string.Empty).Trim().ToLowerInvariant();
    }
}
