using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using VelavanDemo.Application.Authentication.Models;
using VelavanDemo.Application.Common;
using VelavanDemo.Application.Security;
using VelavanDemo.Application.Users.Models;
using VelavanDemo.Domain.Authorization;
using VelavanDemo.Domain.Enums;
using VelavanDemo.Infrastructure.Persistence;

namespace VelavanDemo.IntegrationTests;

public sealed class AdminUserManagementTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AdminUserManagementTests(AuthApiFactory factory)
    {
        _factory = factory;
        _factory.EnsureStarted();
        _client = CreateApiClient();
    }

    [Fact]
    public async Task GetUsers_SupportsSearchStatusRoleAndPagination()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);
        var prefix = $"filter-{Guid.NewGuid():N}";

        var first = await _factory.CreateUserAsync(
            username: $"{prefix}-1",
            status: UserStatus.Active);
        var second = await _factory.CreateUserAsync(
            username: $"{prefix}-2",
            status: UserStatus.Active);
        var third = await _factory.CreateUserAsync(
            username: $"{prefix}-3",
            status: UserStatus.Active);
        var inactive = await _factory.CreateUserAsync(
            username: $"{prefix}-4",
            status: UserStatus.Inactive);
        var adminTarget = await _factory.CreateUserAsync(
            RoleNames.Admin,
            UserStatus.Active,
            username: $"{prefix}-5");

        using var client = CreateAuthenticatedClient(token);
        var encodedPrefix = Uri.EscapeDataString(prefix);

        var firstPage = await GetUsersAsync(
            client,
            $"/api/admin/users?search={encodedPrefix}&status=Active&role={Uri.EscapeDataString(RoleNames.User)}&page=1&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);

        var firstPageResult = await firstPage.Content.ReadFromJsonAsync<PagedResult<UserResponse>>(JsonOptions);

        Assert.NotNull(firstPageResult);
        Assert.Equal(3, firstPageResult!.TotalCount);
        Assert.Equal(1, firstPageResult.Page);
        Assert.Equal(2, firstPageResult.PageSize);
        Assert.Equal(2, firstPageResult.TotalPages);
        Assert.Equal(2, firstPageResult.Items.Count);
        Assert.All(firstPageResult.Items, user =>
        {
            Assert.Equal(UserStatus.Active, user.Status);
            Assert.Contains(RoleNames.User, user.Roles);
            Assert.Contains(prefix, user.Username, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(firstPageResult.Items, user => user.Id == inactive.Id);

        var secondPage = await GetUsersAsync(
            client,
            $"/api/admin/users?search={encodedPrefix}&status=Active&role={Uri.EscapeDataString(RoleNames.User)}&page=2&pageSize=2");

        var secondPageResult = await secondPage.Content.ReadFromJsonAsync<PagedResult<UserResponse>>(JsonOptions);

        Assert.NotNull(secondPageResult);
        Assert.Single(secondPageResult!.Items);
        Assert.Contains(
            new[] { first.Id, second.Id, third.Id },
            id => id == secondPageResult.Items[0].Id);

        var inactiveResult = await GetUsersAsync(
            client,
            $"/api/admin/users?search={encodedPrefix}&status=Inactive&role={Uri.EscapeDataString(RoleNames.User)}&page=1&pageSize=10");

        var inactivePage = await inactiveResult.Content.ReadFromJsonAsync<PagedResult<UserResponse>>(JsonOptions);

        Assert.NotNull(inactivePage);
        Assert.Equal(1, inactivePage!.TotalCount);
        Assert.Equal(inactive.Id, Assert.Single(inactivePage.Items).Id);

        var adminResult = await GetUsersAsync(
            client,
            $"/api/admin/users?search={encodedPrefix}&role={Uri.EscapeDataString(RoleNames.Admin)}&page=1&pageSize=10");

        var adminPage = await adminResult.Content.ReadFromJsonAsync<PagedResult<UserResponse>>(JsonOptions);

        Assert.NotNull(adminPage);
        Assert.Equal(1, adminPage!.TotalCount);
        Assert.Equal(adminTarget.Id, Assert.Single(adminPage.Items).Id);
    }

    [Fact]
    public async Task GetUser_WithUnknownId_ReturnsNotFound()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.GetAsync("/api/admin/users/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanUpdateStandardUserProfile()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.Admin);
        var target = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);
        var updatedUsername = $"updated-{Guid.NewGuid():N}";
        var updatedEmail = $"updated-{Guid.NewGuid():N}@integration.test";

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PutAsJsonAsync(
            $"/api/admin/users/{target.Id}",
            new UpdateUserRequest
            {
                Username = updatedUsername,
                Email = updatedEmail,
                FirstName = "Updated",
                LastName = "Person",
                PhoneNumber = "+91 90000 00000"
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<UserResponse>(JsonOptions);

        Assert.NotNull(updated);
        Assert.Equal(updatedUsername, updated!.Username);
        Assert.Equal(updatedEmail, updated.Email);
        Assert.Equal("Updated", updated.FirstName);
        Assert.Equal("Person", updated.LastName);
        Assert.Equal("+91 90000 00000", updated.PhoneNumber);
        Assert.Contains(RoleNames.User, updated.Roles);

        var persistedResponse = await client.GetAsync($"/api/admin/users/{target.Id}");
        var persisted = await persistedResponse.Content.ReadFromJsonAsync<UserResponse>(JsonOptions);

        Assert.NotNull(persisted);
        Assert.Equal(updatedUsername, persisted!.Username);
        Assert.Equal(updatedEmail, persisted.Email);
        Assert.Equal("Updated", persisted.FirstName);
    }

    [Fact]
    public async Task Admin_CannotUpdatePrivilegedUserProfile()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.Admin);
        var targetAdmin = await _factory.CreateUserAsync(RoleNames.Admin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PutAsJsonAsync(
            $"/api/admin/users/{targetAdmin.Id}",
            new UpdateUserRequest
            {
                Username = targetAdmin.Username,
                Email = targetAdmin.Email,
                FirstName = "Blocked",
                LastName = "Change",
                PhoneNumber = null
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CanUpdatePrivilegedUserProfile()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var targetAdmin = await _factory.CreateUserAsync(RoleNames.Admin);
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PutAsJsonAsync(
            $"/api/admin/users/{targetAdmin.Id}",
            new UpdateUserRequest
            {
                Username = targetAdmin.Username,
                Email = targetAdmin.Email,
                FirstName = "Super",
                LastName = "Updated",
                PhoneNumber = null
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<UserResponse>(JsonOptions);

        Assert.NotNull(updated);
        Assert.Equal("Super", updated!.FirstName);
        Assert.Contains(RoleNames.Admin, updated.Roles);
    }

    [Fact]
    public async Task CreateUser_WithDuplicateUsername_ReturnsConflict()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var existing = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PostAsJsonAsync(
            "/api/admin/users",
            new CreateUserRequest
            {
                Username = existing.Username.ToUpperInvariant(),
                FirstName = "Duplicate",
                LastName = "Username",
                Password = AuthApiFactory.DefaultPassword,
                Roles = new List<string> { RoleNames.User }
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WithoutEmail_Succeeds()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PostAsJsonAsync(
            "/api/admin/users",
            new CreateUserRequest
            {
                Username = $"no-email-{Guid.NewGuid():N}",
                FirstName = "No",
                LastName = "Email",
                Password = AuthApiFactory.DefaultPassword,
                Roles = new List<string> { RoleNames.User }
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<UserResponse>(JsonOptions);

        Assert.NotNull(created);
        Assert.Null(created!.Email);
        Assert.Equal(RoleNames.User, Assert.Single(created.Roles));
    }

    [Fact]
    public async Task UpdateUser_WithDuplicateUsername_ReturnsConflict()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var first = await _factory.CreateUserAsync();
        var second = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PutAsJsonAsync(
            $"/api/admin/users/{first.Id}",
            new UpdateUserRequest
            {
                Username = second.Username.ToUpperInvariant(),
                FirstName = "Duplicate",
                LastName = "Username",
                PhoneNumber = null
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_WithDuplicateEmail_ReturnsConflict()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var first = await _factory.CreateUserAsync();
        var second = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PutAsJsonAsync(
            $"/api/admin/users/{first.Id}",
            new UpdateUserRequest
            {
                Username = first.Username,
                Email = second.Email!.ToUpperInvariant(),
                FirstName = "Duplicate",
                LastName = "Email",
                PhoneNumber = null
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_WithBlankName_ReturnsBadRequest()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.Admin);
        var target = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PutAsJsonAsync(
            $"/api/admin/users/{target.Id}",
            new UpdateUserRequest
            {
                Username = target.Username,
                Email = target.Email,
                FirstName = "   ",
                LastName = "Person",
                PhoneNumber = null
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WithDuplicateEmail_ReturnsConflict()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var existing = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PostAsJsonAsync(
            "/api/admin/users",
            new CreateUserRequest
            {
                Username = $"duplicate-email-{Guid.NewGuid():N}",
                Email = existing.Email,
                FirstName = "Duplicate",
                LastName = "User",
                Password = AuthApiFactory.DefaultPassword,
                Roles = new List<string> { RoleNames.User }
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WithWeakPassword_ReturnsBadRequest()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PostAsJsonAsync(
            "/api/admin/users",
            new CreateUserRequest
            {
                Username = $"weak-{Guid.NewGuid():N}",
                Email = $"weak-{Guid.NewGuid():N}@integration.test",
                FirstName = "Weak",
                LastName = "Password",
                Password = "weak",
                Roles = new List<string> { RoleNames.User }
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanActivateStandardUser()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.Admin);
        var target = await _factory.CreateUserAsync(status: UserStatus.Inactive);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PatchAsJsonAsync(
            $"/api/admin/users/{target.Id}/status",
            new UpdateUserStatusRequest { Status = UserStatus.Active },
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<UserResponse>(JsonOptions);

        Assert.NotNull(updated);
        Assert.Equal(UserStatus.Active, updated!.Status);
    }

    [Fact]
    public async Task SuperAdmin_CannotDeactivateLastActiveSuperAdmin()
    {
        var target = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var actingUser = await _factory.CreateUserAsync(RoleNames.User);

        await DeactivateOtherActiveSuperAdminsAsync(target.Id);

        var token = CreateToken(actingUser, RoleNames.SuperAdmin);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PatchAsJsonAsync(
            $"/api/admin/users/{target.Id}/status",
            new UpdateUserStatusRequest { Status = UserStatus.Inactive },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedUser_CannotAccessAdminUsers()
    {
        var response = await _client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_ReturnsBadRequest()
    {
        var user = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(user.Username, user.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new ChangePasswordRequest
            {
                CurrentPassword = "WrongPass!2345",
                NewPassword = "ChangedPass!2345"
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithWeakNewPassword_ReturnsBadRequest()
    {
        var user = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(user.Username, user.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new ChangePasswordRequest
            {
                CurrentPassword = user.Password,
                NewPassword = "weak"
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient CreateApiClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private Task<HttpResponseMessage> GetUsersAsync(HttpClient client, string url)
    {
        return client.GetAsync(url);
    }

    private async Task<string> LoginAndGetTokenAsync(string username, string password)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Username = username, Password = password },
            JsonOptions);

        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        Assert.NotNull(auth);
        return auth!.AccessToken;
    }

    private async Task DeactivateOtherActiveSuperAdminsAsync(int keepUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var otherSuperAdmins = await dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .Where(user =>
                user.Id != keepUserId &&
                user.Status == UserStatus.Active &&
                user.UserRoles.Any(userRole => userRole.Role.Name == RoleNames.SuperAdmin))
            .ToListAsync();

        foreach (var superAdmin in otherSuperAdmins)
        {
            superAdmin.Status = UserStatus.Inactive;
        }

        await dbContext.SaveChangesAsync();
    }

    private static string CreateToken(TestUser user, string roleName)
    {
        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(AuthApiFactory.TestJwtSecret));
        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: AuthApiFactory.TestJwtIssuer,
            audience: AuthApiFactory.TestJwtAudience,
            claims: new[]
            {
                new Claim(
                    AuthClaimTypes.UserId,
                    user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(AuthClaimTypes.Username, user.Username),
                new Claim(AuthClaimTypes.Email, user.Email ?? user.Username),
                new Claim(AuthClaimTypes.Name, "Integration Test User"),
                new Claim(AuthClaimTypes.Role, roleName)
            },
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(10),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
