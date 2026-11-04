using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VelavanDemo.Application.Common;
using VelavanDemo.Application.Security;
using VelavanDemo.Application.Users;
using VelavanDemo.Application.Users.Models;
using VelavanDemo.Domain.Authorization;
using VelavanDemo.Domain.Entities;
using VelavanDemo.Domain.Enums;
using VelavanDemo.Infrastructure.Persistence;

namespace VelavanDemo.Infrastructure.Users;

public sealed class UserManagementService : IUserManagementService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        ApplicationDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        ILogger<UserManagementService> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<PagedResult<UserResponse>> GetUsersAsync(
        UserQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var usersQuery = _dbContext.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            var normalizedSearch = UsernameNormalizer.Normalize(search);

            usersQuery = usersQuery.Where(user =>
                user.NormalizedUsername.Contains(normalizedSearch) ||
                user.FirstName.Contains(search) ||
                user.LastName.Contains(search) ||
                (user.Email != null && user.Email.Contains(normalizedSearch)));
        }

        if (query.Status.HasValue)
        {
            usersQuery = usersQuery.Where(user => user.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var requestedRole = query.Role.Trim();

            var canonicalRole = RoleNames.All.FirstOrDefault(
                roleName => string.Equals(roleName, requestedRole, StringComparison.OrdinalIgnoreCase));

            if (canonicalRole is null)
            {
                usersQuery = usersQuery.Where(user => false);
            }
            else
            {
                usersQuery = usersQuery.Where(user =>
                    user.UserRoles.Any(userRole => userRole.Role.Name == canonicalRole));
            }
        }

        var totalCount = await usersQuery.CountAsync(cancellationToken);

        var users = await usersQuery
            .OrderBy(user => user.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<UserResponse>(
            users.Select(UserResponseMapper.Map).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<OperationResult<UserResponse>> GetUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(candidate => candidate.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        return user is null
            ? OperationResult<UserResponse>.Failure(OperationError.NotFound, "User was not found.")
            : OperationResult<UserResponse>.Success(UserResponseMapper.Map(user));
    }

    public async Task<OperationResult<UserResponse>> CreateUserAsync(
        CreateUserRequest request,
        int actingUserId,
        bool isSuperAdmin,
        CancellationToken cancellationToken = default)
    {
        var username = request.Username.Trim();
        var normalizedUsername = UsernameNormalizer.Normalize(request.Username);
        var email = NormalizeOptionalEmail(request.Email);

        if (normalizedUsername.Length == 0)
        {
            return OperationResult<UserResponse>.Failure(
                OperationError.Validation,
                "Username is required.");
        }

        var usernameExists = await _dbContext.Users
            .AnyAsync(user => user.NormalizedUsername == normalizedUsername, cancellationToken);

        if (usernameExists)
        {
            return OperationResult<UserResponse>.Failure(
                OperationError.DuplicateUsername,
                "A user with this username already exists.");
        }

        if (email is not null)
        {
            var emailExists = await _dbContext.Users
                .AnyAsync(user => user.Email == email, cancellationToken);

            if (emailExists)
            {
                return OperationResult<UserResponse>.Failure(
                    OperationError.DuplicateEmail,
                    "A user with this email address already exists.");
            }
        }

        var passwordErrors = PasswordPolicy.Validate(request.Password);

        if (passwordErrors.Count > 0)
        {
            return OperationResult<UserResponse>.Failure(
                OperationError.Validation,
                string.Join(" ", passwordErrors));
        }

        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        if (firstName.Length == 0 || lastName.Length == 0)
        {
            return OperationResult<UserResponse>.Failure(
                OperationError.Validation,
                "First name and last name are required.");
        }

        var roleResolution = await ResolveRolesAsync(request.Roles, isSuperAdmin, cancellationToken);

        if (roleResolution.Roles is null)
        {
            return OperationResult<UserResponse>.Failure(roleResolution.Error, roleResolution.Message!);
        }

        var user = new User
        {
            Username = username,
            NormalizedUsername = normalizedUsername,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            Status = UserStatus.Active
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        foreach (var role in roleResolution.Roles)
        {
            user.UserRoles.Add(new UserRole { Role = role });
        }

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} was created by {ActingUserId}.",
            user.Id,
            actingUserId);

        return OperationResult<UserResponse>.Success(UserResponseMapper.Map(user));
    }

    public async Task<OperationResult<UserResponse>> UpdateUserAsync(
        int userId,
        UpdateUserRequest request,
        int actingUserId,
        bool isSuperAdmin,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Include(candidate => candidate.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult<UserResponse>.Failure(OperationError.NotFound, "User was not found.");
        }

        var targetIsPrivileged = user.UserRoles.Any(
            userRole => userRole.Role.Name is RoleNames.Admin or RoleNames.SuperAdmin);

        if (!isSuperAdmin && targetIsPrivileged)
        {
            return OperationResult<UserResponse>.Failure(
                OperationError.Forbidden,
                "Administrators can update standard users only.");
        }

        var username = request.Username.Trim();
        var normalizedUsername = UsernameNormalizer.Normalize(request.Username);
        var email = NormalizeOptionalEmail(request.Email);

        if (normalizedUsername.Length == 0)
        {
            return OperationResult<UserResponse>.Failure(
                OperationError.Validation,
                "Username is required.");
        }

        var usernameExists = await _dbContext.Users
            .AnyAsync(
                candidate =>
                    candidate.Id != userId &&
                    candidate.NormalizedUsername == normalizedUsername,
                cancellationToken);

        if (usernameExists)
        {
            return OperationResult<UserResponse>.Failure(
                OperationError.DuplicateUsername,
                "A user with this username already exists.");
        }

        if (email is not null)
        {
            var emailExists = await _dbContext.Users
                .AnyAsync(
                    candidate => candidate.Id != userId && candidate.Email == email,
                    cancellationToken);

            if (emailExists)
            {
                return OperationResult<UserResponse>.Failure(
                    OperationError.DuplicateEmail,
                    "A user with this email address already exists.");
            }
        }

        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        if (firstName.Length == 0 || lastName.Length == 0)
        {
            return OperationResult<UserResponse>.Failure(
                OperationError.Validation,
                "First name and last name are required.");
        }

        user.Username = username;
        user.NormalizedUsername = normalizedUsername;
        user.Email = email;
        user.FirstName = firstName;
        user.LastName = lastName;
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} was updated by {ActingUserId}.",
            user.Id,
            actingUserId);

        return OperationResult<UserResponse>.Success(UserResponseMapper.Map(user));
    }

    public async Task<OperationResult<UserResponse>> UpdateUserRolesAsync(
        int userId,
        UpdateUserRolesRequest request,
        int actingUserId,
        bool isSuperAdmin,
        CancellationToken cancellationToken = default)
    {
        if (!isSuperAdmin)
        {
            return OperationResult<UserResponse>.Failure(
                OperationError.Forbidden,
                "Only a Super Admin can change user roles.");
        }

        var user = await _dbContext.Users
            .Include(candidate => candidate.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult<UserResponse>.Failure(OperationError.NotFound, "User was not found.");
        }

        var roleResolution = await ResolveRolesAsync(request.Roles, isSuperAdmin, cancellationToken);

        if (roleResolution.Roles is null)
        {
            return OperationResult<UserResponse>.Failure(roleResolution.Error, roleResolution.Message!);
        }

        var currentlySuperAdmin = user.UserRoles.Any(
            userRole => userRole.Role.Name == RoleNames.SuperAdmin);

        var willBeSuperAdmin = roleResolution.Roles.Any(role => role.Name == RoleNames.SuperAdmin);

        if (currentlySuperAdmin && !willBeSuperAdmin)
        {
            if (user.Id == actingUserId)
            {
                return OperationResult<UserResponse>.Failure(
                    OperationError.SelfModification,
                    "You cannot remove the Super Admin role from your own account.");
            }

            var anotherActiveSuperAdminExists = await _dbContext.Users.AnyAsync(
                candidate =>
                    candidate.Id != user.Id &&
                    candidate.Status == UserStatus.Active &&
                    candidate.UserRoles.Any(userRole => userRole.Role.Name == RoleNames.SuperAdmin),
                cancellationToken);

            if (!anotherActiveSuperAdminExists)
            {
                return OperationResult<UserResponse>.Failure(
                    OperationError.LastSuperAdmin,
                    "At least one active Super Admin account must remain.");
            }
        }

        var requestedRoleIds = roleResolution.Roles.Select(role => role.Id).ToHashSet();
        var existingRoleIds = user.UserRoles.Select(userRole => userRole.RoleId).ToHashSet();

        var linksToRemove = user.UserRoles
            .Where(userRole => !requestedRoleIds.Contains(userRole.RoleId))
            .ToArray();

        foreach (var link in linksToRemove)
        {
            _dbContext.UserRoles.Remove(link);
            user.UserRoles.Remove(link);
        }

        foreach (var role in roleResolution.Roles.Where(role => !existingRoleIds.Contains(role.Id)))
        {
            user.UserRoles.Add(new UserRole { RoleId = role.Id, Role = role });
        }

        user.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Roles for user {UserId} were updated by {ActingUserId}.",
            user.Id,
            actingUserId);

        return OperationResult<UserResponse>.Success(UserResponseMapper.Map(user));
    }

    public async Task<OperationResult<UserResponse>> UpdateUserStatusAsync(
        int userId,
        UpdateUserStatusRequest request,
        int actingUserId,
        bool isSuperAdmin,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Include(candidate => candidate.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult<UserResponse>.Failure(OperationError.NotFound, "User was not found.");
        }

        if (user.Id == actingUserId && user.Status != request.Status)
        {
            return OperationResult<UserResponse>.Failure(
                OperationError.SelfModification,
                "You cannot change your own account status.");
        }

        var targetIsPrivileged = user.UserRoles.Any(
            userRole => userRole.Role.Name is RoleNames.Admin or RoleNames.SuperAdmin);

        if (!isSuperAdmin && targetIsPrivileged)
        {
            return OperationResult<UserResponse>.Failure(
                OperationError.Forbidden,
                "Administrators can change the status of standard users only.");
        }

        var targetIsActiveSuperAdmin =
            user.Status == UserStatus.Active &&
            user.UserRoles.Any(userRole => userRole.Role.Name == RoleNames.SuperAdmin);

        if (targetIsActiveSuperAdmin && request.Status != UserStatus.Active)
        {
            var anotherActiveSuperAdminExists = await _dbContext.Users.AnyAsync(
                candidate =>
                    candidate.Id != user.Id &&
                    candidate.Status == UserStatus.Active &&
                    candidate.UserRoles.Any(userRole => userRole.Role.Name == RoleNames.SuperAdmin),
                cancellationToken);

            if (!anotherActiveSuperAdminExists)
            {
                return OperationResult<UserResponse>.Failure(
                    OperationError.LastSuperAdmin,
                    "At least one active Super Admin account must remain.");
            }
        }

        if (user.Status != request.Status)
        {
            user.Status = request.Status;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Status for user {UserId} was changed to {Status} by {ActingUserId}.",
                user.Id,
                user.Status,
                actingUserId);
        }

        return OperationResult<UserResponse>.Success(UserResponseMapper.Map(user));
    }

    private static string? NormalizeOptionalEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email) ? null : EmailNormalizer.Normalize(email);
    }

    private async Task<(List<Role>? Roles, OperationError Error, string? Message)> ResolveRolesAsync(
        IEnumerable<string> requestedRoleNames,
        bool isSuperAdmin,
        CancellationToken cancellationToken)
    {
        var canonicalRoleNames = new List<string>();

        foreach (var requestedRoleName in requestedRoleNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var trimmedRoleName = requestedRoleName.Trim();

            var canonicalRoleName = RoleNames.All.FirstOrDefault(
                roleName => string.Equals(roleName, trimmedRoleName, StringComparison.OrdinalIgnoreCase));

            if (canonicalRoleName is null)
            {
                return (null, OperationError.InvalidRole, $"Unknown role '{trimmedRoleName}'.");
            }

            if (!canonicalRoleNames.Contains(canonicalRoleName, StringComparer.Ordinal))
            {
                canonicalRoleNames.Add(canonicalRoleName);
            }
        }

        if (canonicalRoleNames.Count == 0)
        {
            canonicalRoleNames.Add(RoleNames.User);
        }

        if (!isSuperAdmin && canonicalRoleNames.Any(roleName => roleName != RoleNames.User))
        {
            return (null, OperationError.Forbidden, "Administrators can assign only the User role.");
        }

        var roles = await _dbContext.Roles
            .Where(role => canonicalRoleNames.Contains(role.Name))
            .ToListAsync(cancellationToken);

        if (roles.Count != canonicalRoleNames.Count)
        {
            return (null, OperationError.InvalidRole, "One or more requested roles are not available.");
        }

        return (roles, OperationError.None, null);
    }
}
