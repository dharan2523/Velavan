using VelavanDemo.Application.Users.Models;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Users;

internal static class UserResponseMapper
{
    public static UserResponse Map(User user)
    {
        return Map(user, user.UserRoles.Select(userRole => userRole.Role.Name));
    }

    public static UserResponse Map(User user, IEnumerable<string> roles)
    {
        return new UserResponse(
            user.Id,
            user.Username,
            user.Email,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            user.Status,
            roles
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(role => role)
                .ToArray(),
            user.CreatedAtUtc,
            user.UpdatedAtUtc,
            user.LastLoginAtUtc);
    }
}
