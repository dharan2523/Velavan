namespace VelavanDemo.Domain.Authorization;

public static class RoleNames
{
    public const string SuperAdmin = "Super Admin";
    public const string Admin = "Admin";
    public const string User = "User";

    public static IReadOnlyList<string> All { get; } = new[] { SuperAdmin, Admin, User };
}
