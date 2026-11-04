using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VelavanDemo.Application.Authentication;
using VelavanDemo.Application.Authentication.Models;
using VelavanDemo.Application.Common;
using VelavanDemo.Application.Security;
using VelavanDemo.Application.Users.Models;
using VelavanDemo.Domain.Entities;
using VelavanDemo.Domain.Enums;
using VelavanDemo.Infrastructure.Persistence;
using VelavanDemo.Infrastructure.Users;

namespace VelavanDemo.Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        ApplicationDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _logger = logger;
    }

    public async Task<AuthenticationResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var username = UsernameNormalizer.Normalize(request.Username);

        var user = await _dbContext.Users
            .Include(candidate => candidate.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(
                candidate => candidate.NormalizedUsername == username,
                cancellationToken);

        if (user is null)
        {
            _logger.LogWarning("Login failed because the supplied username was not found.");
            return AuthenticationResult.Failed(AuthenticationFailure.InvalidCredentials);
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Login failed due to an invalid password for user {UserId}.", user.Id);
            return AuthenticationResult.Failed(AuthenticationFailure.InvalidCredentials);
        }

        if (user.Status != UserStatus.Active)
        {
            _logger.LogWarning("Login blocked for inactive user {UserId}.", user.Id);
            return AuthenticationResult.Failed(AuthenticationFailure.AccountInactive);
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var roleNames = GetRoleNames(user);
        var displayName = $"{user.FirstName} {user.LastName}".Trim();
        var token = _jwtTokenGenerator.GenerateToken(
            user.Id,
            user.Username,
            user.Email,
            displayName,
            roleNames);

        _logger.LogInformation("User {UserId} logged in.", user.Id);

        return AuthenticationResult.Success(
            new AuthResponse(
                token.AccessToken,
                "Bearer",
                token.ExpiresAtUtc,
                UserResponseMapper.Map(user, roleNames)));
    }

    public async Task<UserResponse?> GetCurrentUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(candidate => candidate.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null || user.Status != UserStatus.Active)
        {
            return null;
        }

        return UserResponseMapper.Map(user);
    }

    public async Task<OperationResult> ChangePasswordAsync(
        int userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult.Failure(OperationError.NotFound, "User was not found.");
        }

        if (user.Status != UserStatus.Active)
        {
            return OperationResult.Failure(OperationError.Forbidden, "The user account is not active.");
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.CurrentPassword);

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return OperationResult.Failure(
                OperationError.CurrentPasswordIncorrect,
                "The current password is incorrect.");
        }

        if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
        {
            return OperationResult.Failure(
                OperationError.PasswordReuse,
                "The new password must be different from the current password.");
        }

        var passwordErrors = PasswordPolicy.Validate(request.NewPassword);

        if (passwordErrors.Count > 0)
        {
            return OperationResult.Failure(OperationError.Validation, string.Join(" ", passwordErrors));
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Password changed for user {UserId}.", user.Id);

        return OperationResult.Success();
    }

    private static string[] GetRoleNames(User user)
    {
        return user.UserRoles
            .Select(userRole => userRole.Role.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(roleName => roleName)
            .ToArray();
    }
}
