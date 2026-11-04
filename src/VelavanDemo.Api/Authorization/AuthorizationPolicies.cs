namespace VelavanDemo.Api.Authorization;

public static class AuthorizationPolicies
{
    public const string AdminOrSuperAdmin = "AdminOrSuperAdmin";
    public const string SuperAdminOnly = "SuperAdminOnly";
}
