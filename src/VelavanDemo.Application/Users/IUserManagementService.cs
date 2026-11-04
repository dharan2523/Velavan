using VelavanDemo.Application.Common;
using VelavanDemo.Application.Users.Models;

namespace VelavanDemo.Application.Users;

public interface IUserManagementService
{
    Task<PagedResult<UserResponse>> GetUsersAsync(
        UserQuery query,
        CancellationToken cancellationToken = default);

    Task<OperationResult<UserResponse>> GetUserAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<OperationResult<UserResponse>> CreateUserAsync(
        CreateUserRequest request,
        int actingUserId,
        bool isSuperAdmin,
        CancellationToken cancellationToken = default);

    Task<OperationResult<UserResponse>> UpdateUserAsync(
        int userId,
        UpdateUserRequest request,
        int actingUserId,
        bool isSuperAdmin,
        CancellationToken cancellationToken = default);

    Task<OperationResult<UserResponse>> UpdateUserRolesAsync(
        int userId,
        UpdateUserRolesRequest request,
        int actingUserId,
        bool isSuperAdmin,
        CancellationToken cancellationToken = default);

    Task<OperationResult<UserResponse>> UpdateUserStatusAsync(
        int userId,
        UpdateUserStatusRequest request,
        int actingUserId,
        bool isSuperAdmin,
        CancellationToken cancellationToken = default);
}
