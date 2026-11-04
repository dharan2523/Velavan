using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using VelavanDemo.Application.Authentication.Models;
using VelavanDemo.Application.Common;
using VelavanDemo.Application.Security;
using VelavanDemo.Application.Users.Models;
using VelavanDemo.Domain.Authorization;
using VelavanDemo.Domain.Enums;

namespace VelavanDemo.IntegrationTests;

public sealed class AuthenticationEndpointsTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AuthenticationEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _factory.EnsureStarted();
        _client = CreateApiClient();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsJwtAndUser()
    {
        var user = await _factory.CreateUserAsync();

        var response = await LoginAsync(user.Username, user.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth!.AccessToken));
        Assert.Equal("Bearer", auth.TokenType);
        Assert.Equal(user.Username, auth.User.Username);
        Assert.Equal(user.Email, auth.User.Email);
        Assert.Contains(RoleNames.User, auth.User.Roles);
        Assert.True(auth.ExpiresAtUtc > DateTime.UtcNow);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken);

        Assert.Contains(
            token.Claims,
            claim =>
                claim.Type == AuthClaimTypes.Username &&
                claim.Value == user.Username);
    }

    [Fact]
    public async Task Login_WithDifferentUsernameCase_ReturnsOk()
    {
        var user = await _factory.CreateUserAsync();

        var response = await LoginAsync(user.Username.ToUpperInvariant(), user.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        Assert.NotNull(auth);
        Assert.Equal(user.Username, auth!.User.Username);
    }

    [Fact]
    public async Task Login_WithEmailInsteadOfUsername_ReturnsUnauthorized()
    {
        var user = await _factory.CreateUserAsync();
        Assert.NotNull(user.Email);

        var response = await LoginAsync(user.Email!, user.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var user = await _factory.CreateUserAsync();

        var response = await LoginAsync(user.Username, "WrongPass!2345");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithInactiveUser_ReturnsForbidden()
    {
        var user = await _factory.CreateUserAsync(status: UserStatus.Inactive);

        var response = await LoginAsync(user.Username, user.Password);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsCurrentUser()
    {
        var user = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(user.Username, user.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var currentUser = await response.Content.ReadFromJsonAsync<UserResponse>(JsonOptions);

        Assert.NotNull(currentUser);
        Assert.Equal(user.Id, currentUser!.Id);
        Assert.Equal(user.Username, currentUser.Username);
        Assert.Equal(user.Email, currentUser.Email);
    }

    [Fact]
    public async Task UserToken_CannotAccessAdminEndpoints()
    {
        var user = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(user.Username, user.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminToken_CanAccessAdminEndpoint()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.Admin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var users = await response.Content.ReadFromJsonAsync<PagedResult<UserResponse>>(JsonOptions);

        Assert.NotNull(users);
        Assert.Contains(users!.Items, item => item.Id == admin.Id);
    }

    [Fact]
    public async Task Admin_CannotAssignPrivilegedRole()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.Admin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);

        using var client = CreateAuthenticatedClient(token);
        var request = new CreateUserRequest
        {
            Username = $"blocked-{Guid.NewGuid():N}",
            Email = $"blocked-{Guid.NewGuid():N}@integration.test",
            FirstName = "Blocked",
            LastName = "User",
            Password = AuthApiFactory.DefaultPassword,
            Roles = new List<string> { RoleNames.Admin }
        };

        var response = await client.PostAsJsonAsync("/api/admin/users", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CannotUpdateUserRoles()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.Admin);
        var target = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PutAsJsonAsync(
            $"/api/admin/users/{target.Id}/roles",
            new UpdateUserRolesRequest { Roles = new List<string> { RoleNames.User } },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CanCreateUserWithAdminRole()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);

        using var client = CreateAuthenticatedClient(token);
        var request = new CreateUserRequest
        {
            Username = $"new-admin-{Guid.NewGuid():N}",
            Email = $"new-admin-{Guid.NewGuid():N}@integration.test",
            FirstName = "New",
            LastName = "Admin",
            Password = AuthApiFactory.DefaultPassword,
            Roles = new List<string> { RoleNames.Admin }
        };

        var response = await client.PostAsJsonAsync("/api/admin/users", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var createdUser = await response.Content.ReadFromJsonAsync<UserResponse>(JsonOptions);

        Assert.NotNull(createdUser);
        Assert.Contains(RoleNames.Admin, createdUser!.Roles);
    }

    [Fact]
    public async Task SuperAdmin_CanUpdateUserRoles()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var target = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PutAsJsonAsync(
            $"/api/admin/users/{target.Id}/roles",
            new UpdateUserRolesRequest { Roles = new List<string> { RoleNames.Admin } },
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updatedUser = await response.Content.ReadFromJsonAsync<UserResponse>(JsonOptions);

        Assert.NotNull(updatedUser);
        Assert.Contains(RoleNames.Admin, updatedUser!.Roles);
    }

    [Fact]
    public async Task Admin_CannotChangePrivilegedUserStatus()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.Admin);
        var targetAdmin = await _factory.CreateUserAsync(RoleNames.Admin);
        var token = await LoginAndGetTokenAsync(admin.Username, admin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PatchAsJsonAsync(
            $"/api/admin/users/{targetAdmin.Id}/status",
            new UpdateUserStatusRequest { Status = UserStatus.Inactive },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CannotDeactivateOwnAccount()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PatchAsJsonAsync(
            $"/api/admin/users/{superAdmin.Id}/status",
            new UpdateUserStatusRequest { Status = UserStatus.Inactive },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CannotRemoveOwnSuperAdminRole()
    {
        var superAdmin = await _factory.CreateUserAsync(RoleNames.SuperAdmin);
        var token = await LoginAndGetTokenAsync(superAdmin.Username, superAdmin.Password);

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PutAsJsonAsync(
            $"/api/admin/users/{superAdmin.Id}/roles",
            new UpdateUserRolesRequest { Roles = new List<string> { RoleNames.Admin } },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithValidCurrentPassword_ReplacesPassword()
    {
        var user = await _factory.CreateUserAsync();
        var token = await LoginAndGetTokenAsync(user.Username, user.Password);
        var newPassword = "ChangedPass!2345";

        using var client = CreateAuthenticatedClient(token);
        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new ChangePasswordRequest
            {
                CurrentPassword = user.Password,
                NewPassword = newPassword
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(user.Username, user.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Username, newPassword)).StatusCode);
    }

    [Fact]
    public async Task DeactivatedUser_ExistingTokenIsRejected()
    {
        var admin = await _factory.CreateUserAsync(RoleNames.Admin);
        var target = await _factory.CreateUserAsync();
        var targetToken = await LoginAndGetTokenAsync(target.Username, target.Password);
        var adminToken = await LoginAndGetTokenAsync(admin.Username, admin.Password);

        using var adminClient = CreateAuthenticatedClient(adminToken);
        var deactivateResponse = await adminClient.PatchAsJsonAsync(
            $"/api/admin/users/{target.Id}/status",
            new UpdateUserStatusRequest { Status = UserStatus.Inactive },
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        using var targetClient = CreateAuthenticatedClient(targetToken);
        var meResponse = await targetClient.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var user = await _factory.CreateUserAsync();
        var expiredToken = CreateExpiredToken(user);

        using var client = CreateAuthenticatedClient(expiredToken);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    private Task<HttpResponseMessage> LoginAsync(string username, string password)
    {
        return _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Username = username, Password = password },
            JsonOptions);
    }

    private async Task<string> LoginAndGetTokenAsync(string username, string password)
    {
        var response = await LoginAsync(username, password);
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        Assert.NotNull(auth);
        return auth!.AccessToken;
    }

    private static string CreateExpiredToken(TestUser user)
    {
        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(AuthApiFactory.TestJwtSecret));
        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: AuthApiFactory.TestJwtIssuer,
            audience: AuthApiFactory.TestJwtAudience,
            claims: new[]
            {
                new Claim(AuthClaimTypes.UserId, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(AuthClaimTypes.Username, user.Username),
                new Claim(AuthClaimTypes.Email, user.Email ?? user.Username),
                new Claim(AuthClaimTypes.Name, "Expired Test User"),
                new Claim(AuthClaimTypes.Role, RoleNames.User)
            },
            notBefore: now.AddMinutes(-10),
            expires: now.AddMinutes(-5),
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
