namespace VelavanDemo.Application.Authentication;

public sealed record JwtTokenResult(string AccessToken, DateTime ExpiresAtUtc);

public interface IJwtTokenGenerator
{
    JwtTokenResult GenerateToken(
        int userId,
        string username,
        string? email,
        string displayName,
        IEnumerable<string> roles);
}
