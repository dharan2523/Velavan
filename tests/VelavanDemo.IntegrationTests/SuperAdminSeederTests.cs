using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VelavanDemo.Domain.Authorization;
using VelavanDemo.Domain.Entities;
using VelavanDemo.Domain.Enums;
using VelavanDemo.Infrastructure.Persistence;
using VelavanDemo.Infrastructure.Seed;

namespace VelavanDemo.IntegrationTests;

public sealed class SuperAdminSeederTests
{
    [Fact]
    public async Task StartAsync_UpdatesLegacySuperAdminUsername_WithoutChangingOtherState()
    {
        const string legacyEmail = "legacy-super-admin@integration.test";
        const string passwordHash = "existing-password-hash";
        const string configuredUsername = "root-admin";

        await using var provider = CreateProvider();
        var userId = await SeedLegacySuperAdminAsync(provider, legacyEmail, passwordHash);
        var seeder = CreateSeeder(provider, configuredUsername);

        await seeder.StartAsync(CancellationToken.None);

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await dbContext.Users
            .Include(candidate => candidate.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleAsync(candidate => candidate.Id == userId);

        Assert.Equal(configuredUsername, user.Username);
        Assert.Equal(configuredUsername, user.NormalizedUsername);
        Assert.Equal(legacyEmail, user.Email);
        Assert.Equal(passwordHash, user.PasswordHash);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.NotNull(user.UpdatedAtUtc);
        Assert.Contains(user.UserRoles, userRole => userRole.Role.Name == RoleNames.SuperAdmin);
    }

    [Fact]
    public async Task StartAsync_WhenRequiredConfigurationIsMissing_ThrowsBeforeDatabaseAccess()
    {
        await using var provider = CreateProvider();
        var seeder = CreateSeeder(provider, username: string.Empty);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.StartAsync(CancellationToken.None));

        Assert.Contains("Seed:SuperAdmin:Username", exception.Message);

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Empty(dbContext.Roles);
        Assert.Empty(dbContext.Users);
    }

    [Fact]
    public async Task StartAsync_DoesNotRenameLegacySuperAdmin_WhenConfiguredUsernameIsTaken()
    {
        const string legacyEmail = "legacy-super-admin@integration.test";
        const string configuredUsername = "taken-username";

        await using var provider = CreateProvider();
        var userId = await SeedLegacySuperAdminAsync(
            provider,
            legacyEmail,
            passwordHash: "existing-password-hash");

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userRole = await GetOrCreateRoleAsync(dbContext, RoleNames.User);

            dbContext.Users.Add(new User
            {
                Username = configuredUsername,
                NormalizedUsername = configuredUsername,
                Email = "taken@integration.test",
                PasswordHash = "other-password-hash",
                FirstName = "Existing",
                LastName = "User",
                Status = UserStatus.Active,
                UserRoles = new List<UserRole>
                {
                    new() { RoleId = userRole.Id, Role = userRole }
                }
            });

            await dbContext.SaveChangesAsync();
        }

        var seeder = CreateSeeder(provider, configuredUsername);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.StartAsync(CancellationToken.None));

        using var verificationScope = provider.CreateScope();
        var verificationDbContext = verificationScope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();
        var unchangedUser = await verificationDbContext.Users
            .SingleAsync(candidate => candidate.Id == userId);

        Assert.Equal(legacyEmail, unchangedUser.Username);
        Assert.Equal(legacyEmail, unchangedUser.NormalizedUsername);
        Assert.Null(unchangedUser.UpdatedAtUtc);
    }

    private static ServiceProvider CreateProvider()
    {
        var databaseName = $"VelavanDemo.SeederTests.{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));

        return services.BuildServiceProvider();
    }

    private static DatabaseSeeder CreateSeeder(
        IServiceProvider provider,
        string username)
    {
        var options = Options.Create(new SuperAdminSeedOptions
        {
            Enabled = true,
            Username = username,
            Email = "optional-contact@integration.test",
            FirstName = "Super",
            LastName = "Admin",
            Password = AuthApiFactory.DefaultPassword
        });

        return new DatabaseSeeder(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            NullLogger<DatabaseSeeder>.Instance);
    }

    private static async Task<int> SeedLegacySuperAdminAsync(
        IServiceProvider provider,
        string email,
        string passwordHash)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var superAdminRole = await GetOrCreateRoleAsync(dbContext, RoleNames.SuperAdmin);

        var user = new User
        {
            Username = normalizedEmail,
            NormalizedUsername = normalizedEmail,
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            FirstName = "Legacy",
            LastName = "SuperAdmin",
            Status = UserStatus.Active,
            UserRoles = new List<UserRole>
            {
                new() { RoleId = superAdminRole.Id, Role = superAdminRole }
            }
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user.Id;
    }

    private static async Task<Role> GetOrCreateRoleAsync(
        ApplicationDbContext dbContext,
        string roleName)
    {
        var role = await dbContext.Roles
            .SingleOrDefaultAsync(candidate => candidate.Name == roleName);

        if (role is not null)
        {
            return role;
        }

        role = new Role
        {
            Name = roleName,
            Description = $"Test {roleName} role",
            IsSystemRole = true
        };

        dbContext.Roles.Add(role);
        await dbContext.SaveChangesAsync();

        return role;
    }
}
