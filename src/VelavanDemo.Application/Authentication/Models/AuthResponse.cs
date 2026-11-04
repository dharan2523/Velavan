using VelavanDemo.Application.Users.Models;

namespace VelavanDemo.Application.Authentication.Models;

public sealed record AuthResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    UserResponse User);
