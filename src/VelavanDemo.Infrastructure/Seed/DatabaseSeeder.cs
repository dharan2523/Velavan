using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VelavanDemo.Application.Common;
using VelavanDemo.Application.Security;
using VelavanDemo.Domain.Authorization;
using VelavanDemo.Domain.Entities;
using VelavanDemo.Domain.Enums;
using VelavanDemo.Infrastructure.Persistence;

namespace VelavanDemo.Infrastructure.Seed;

public sealed class DatabaseSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SuperAdminSeedOptions _superAdminOptions;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        IServiceScopeFactory scopeFactory,
        IOptions<SuperAdminSeedOptions> superAdminOptions,
        ILogger<DatabaseSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _superAdminOptions = superAdminOptions.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_superAdminOptions.Enabled)
        {
            ValidateSuperAdminOptions();
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await EnsureRolesAsync(dbContext, cancellationToken);

        if (_superAdminOptions.Enabled)
        {
            await EnsureSuperAdminAsync(scope.ServiceProvider, dbContext, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task EnsureRolesAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var existingRoleNames = await dbContext.Roles
            .Select(role => role.Name)
            .ToListAsync(cancellationToken);

        foreach (var roleName in RoleNames.All)
        {
            if (existingRoleNames.Contains(roleName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            dbContext.Roles.Add(new Role
            {
                Name = roleName,
                Description = GetRoleDescription(roleName),
                IsSystemRole = true
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureSuperAdminAsync(
        IServiceProvider serviceProvider,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var username = _superAdminOptions.Username.Trim();
        var normalizedUsername = UsernameNormalizer.Normalize(username);
        var email = NormalizeOptionalEmail(_superAdminOptions.Email);

        var superAdminRole = await dbContext.Roles
            .SingleAsync(role => role.Name == RoleNames.SuperAdmin, cancellationToken);

        var existingSuperAdmins = await dbContext.Users
            .Where(user =>
                user.UserRoles.Any(userRole => userRole.RoleId == superAdminRole.Id))
            .ToListAsync(cancellationToken);

        if (existingSuperAdmins.Count > 0)
        {
            await UpdateExistingSuperAdminUsernameAsync(
                dbContext,
                existingSuperAdmins,
                username,
                normalizedUsername,
                cancellationToken);
            return;
        }

        var usernameExists = await dbContext.Users
            .AnyAsync(
                user => user.NormalizedUsername == normalizedUsername,
                cancellationToken);

        if (usernameExists)
        {
            throw new InvalidOperationException(
                "The configured Super Admin seed username is already used by an existing user. No changes were made.");
        }

        if (email is not null)
        {
            var emailExists = await dbContext.Users
                .AnyAsync(user => user.Email == email, cancellationToken);

            if (emailExists)
            {
                throw new InvalidOperationException(
                    "The configured Super Admin seed email is already used by an existing user. No changes were made.");
            }
        }

        var passwordHasher = serviceProvider.GetRequiredService<IPasswordHasher<User>>();

        var user = new User
        {
            Username = username,
            NormalizedUsername = normalizedUsername,
            Email = email,
            FirstName = _superAdminOptions.FirstName.Trim(),
            LastName = _superAdminOptions.LastName.Trim(),
            Status = UserStatus.Active
        };

        user.PasswordHash = passwordHasher.HashPassword(user, _superAdminOptions.Password);
        user.UserRoles.Add(new UserRole { Role = superAdminRole });

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Initial Super Admin account created.");
    }

    private async Task UpdateExistingSuperAdminUsernameAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<User> existingSuperAdmins,
        string username,
        string normalizedUsername,
        CancellationToken cancellationToken)
    {
        var legacySuperAdmins = existingSuperAdmins
            .Where(user =>
                !string.IsNullOrWhiteSpace(user.Email) &&
                string.Equals(
                    UsernameNormalizer.Normalize(user.Username),
                    EmailNormalizer.Normalize(user.Email),
                    StringComparison.Ordinal))
            .ToArray();

        if (legacySuperAdmins.Length == 0)
        {
            _logger.LogInformation(
                "Super Admin username migration skipped because no legacy Super Admin username was found.");
            return;
        }

        if (legacySuperAdmins.Length > 1)
        {
            throw new InvalidOperationException(
                "Multiple legacy Super Admin usernames were found. No username was changed.");
        }

        var superAdmin = legacySuperAdmins[0];

        if (string.Equals(
            superAdmin.NormalizedUsername,
            normalizedUsername,
            StringComparison.Ordinal))
        {
            _logger.LogInformation("Super Admin username migration skipped because it is already configured.");
            return;
        }

        var usernameExists = await dbContext.Users
            .AnyAsync(
                user =>
                    user.Id != superAdmin.Id &&
                    user.NormalizedUsername == normalizedUsername,
                cancellationToken);

        if (usernameExists)
        {
            throw new InvalidOperationException(
                "The configured Super Admin seed username is already used by another user. No username was changed.");
        }

        superAdmin.Username = username;
        superAdmin.NormalizedUsername = normalizedUsername;
        superAdmin.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Existing Super Admin username was updated from its legacy value.");
    }

    private void ValidateSuperAdminOptions()
    {
        var usernamePresent = !string.IsNullOrWhiteSpace(_superAdminOptions.Username);
        var emailPresent = !string.IsNullOrWhiteSpace(_superAdminOptions.Email);
        var firstNamePresent = !string.IsNullOrWhiteSpace(_superAdminOptions.FirstName);
        var lastNamePresent = !string.IsNullOrWhiteSpace(_superAdminOptions.LastName);
        var passwordPresent = !string.IsNullOrWhiteSpace(_superAdminOptions.Password);

        _logger.LogInformation(
            "Super Admin seeding configuration presence: Username={UsernamePresent}, Email={EmailPresent}, " +
            "FirstName={FirstNamePresent}, LastName={LastNamePresent}, Password={PasswordPresent}.",
            usernamePresent,
            emailPresent,
            firstNamePresent,
            lastNamePresent,
            passwordPresent);

        var missingKeys = new List<string>();

        if (!usernamePresent)
        {
            missingKeys.Add("Seed:SuperAdmin:Username");
        }

        if (!firstNamePresent)
        {
            missingKeys.Add("Seed:SuperAdmin:FirstName");
        }

        if (!lastNamePresent)
        {
            missingKeys.Add("Seed:SuperAdmin:LastName");
        }

        if (!passwordPresent)
        {
            missingKeys.Add("Seed:SuperAdmin:Password");
        }

        if (missingKeys.Count > 0)
        {
            throw new InvalidOperationException(
                "Super Admin seeding is enabled but required configuration values are missing: " +
                $"{string.Join(", ", missingKeys)}. Configure them through user-secrets or environment " +
                "variables. No database changes were made.");
        }

        if (_superAdminOptions.Username.Trim().Length > 256)
        {
            throw new InvalidOperationException(
                "The Super Admin seed username must be 256 characters or fewer.");
        }

        var passwordErrors = PasswordPolicy.Validate(_superAdminOptions.Password);

        if (passwordErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"The Super Admin seed password does not meet the password policy: {string.Join(" ", passwordErrors)}");
        }
    }

    private static string? NormalizeOptionalEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email) ? null : EmailNormalizer.Normalize(email);
    }

    private static string GetRoleDescription(string roleName)
    {
        return roleName switch
        {
            RoleNames.SuperAdmin => "Full access to all system features and user administration.",
            RoleNames.Admin => "Administrative access to operational features and standard users.",
            RoleNames.User => "Standard application access.",
            _ => string.Empty
        };
    }
}
