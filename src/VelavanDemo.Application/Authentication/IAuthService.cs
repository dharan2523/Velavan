using VelavanDemo.Application.Authentication.Models;
using VelavanDemo.Application.Common;
using VelavanDemo.Application.Users.Models;

namespace VelavanDemo.Application.Authentication;

public interface IAuthService
{
    Task<AuthenticationResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    Task<UserResponse?> GetCurrentUserAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ChangePasswordAsync(
        int userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}
