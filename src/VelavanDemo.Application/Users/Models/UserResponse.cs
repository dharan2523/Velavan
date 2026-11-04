using VelavanDemo.Domain.Enums;

namespace VelavanDemo.Application.Users.Models;

public sealed record UserResponse(
    int Id,
    string Username,
    string? Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    UserStatus Status,
    IReadOnlyList<string> Roles,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    DateTime? LastLoginAtUtc);
