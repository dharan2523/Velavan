using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VelavanDemo.Application.Attachments;
using VelavanDemo.Application.Authentication;
using VelavanDemo.Application.Users;
using VelavanDemo.Domain.Entities;
using VelavanDemo.Infrastructure.Attachments;
using VelavanDemo.Infrastructure.Authentication;
using VelavanDemo.Infrastructure.Persistence;
using VelavanDemo.Infrastructure.Seed;
using VelavanDemo.Infrastructure.Services;
using VelavanDemo.Infrastructure.Users;

namespace VelavanDemo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString, sqlServerOptions =>
                sqlServerOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SuperAdminSeedOptions>(configuration.GetSection(SuperAdminSeedOptions.SectionName));
        services.Configure<AttachmentStorageOptions>(
            configuration.GetSection(AttachmentStorageOptions.SectionName));

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IAttachmentFileInspector, AttachmentFileInspector>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IUploadedFileScanner, NoOpUploadedFileScanner>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddHostedService<DatabaseSeeder>();

        return services;
    }
}
