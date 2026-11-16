using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
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
        services.Configure<AttachmentScannerOptions>(
            configuration.GetSection(AttachmentScannerOptions.SectionName));
        services.Configure<AttachmentReconciliationOptions>(
            configuration.GetSection(AttachmentReconciliationOptions.SectionName));

        var scannerOptions = new AttachmentScannerOptions
        {
            Provider = configuration[$"{AttachmentScannerOptions.SectionName}:Provider"] ??
                AttachmentScannerOptions.NoOpProvider,
            Endpoint = configuration[$"{AttachmentScannerOptions.SectionName}:Endpoint"],
            ApiKey = configuration[$"{AttachmentScannerOptions.SectionName}:ApiKey"],
            TimeoutSeconds = configuration.GetValue<int>(
                $"{AttachmentScannerOptions.SectionName}:TimeoutSeconds",
                30)
        };
        var environmentName = environment?.EnvironmentName ?? Environments.Development;

        ValidateScannerOptions(scannerOptions, environmentName);

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IAttachmentFileInspector, AttachmentFileInspector>();
        services.AddSingleton<LocalFileStorage>();
        services.AddSingleton<IFileStorage>(provider =>
            provider.GetRequiredService<LocalFileStorage>());
        services.AddSingleton<IFileStorageInventory>(provider =>
            provider.GetRequiredService<LocalFileStorage>());
        services.AddSingleton<IAttachmentConcurrencyGate, AttachmentConcurrencyGate>();

        if (string.Equals(
                scannerOptions.Provider,
                AttachmentScannerOptions.HttpProvider,
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IUploadedFileScanner, HttpUploadedFileScanner>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(scannerOptions.TimeoutSeconds);
            });
        }
        else
        {
            services.AddSingleton<IUploadedFileScanner, NoOpUploadedFileScanner>();
        }

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<AttachmentReconciliationService>();
        services.AddHostedService<DatabaseSeeder>();
        services.AddHostedService<AttachmentReconciliationHostedService>();

        return services;
    }

    private static void ValidateScannerOptions(
        AttachmentScannerOptions options,
        string environmentName)
    {
        var provider = options.Provider?.Trim();

        if (string.IsNullOrWhiteSpace(provider) ||
            string.Equals(provider, AttachmentScannerOptions.NoOpProvider, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, "NoOp", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "A real malware scanner must be configured outside Development and Testing. " +
                    $"Set '{AttachmentScannerOptions.SectionName}:Provider' to 'Http' and configure an endpoint.");
            }

            return;
        }

        if (!string.Equals(provider, AttachmentScannerOptions.HttpProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported malware scanner provider '{options.Provider}'.");
        }

        if (string.IsNullOrWhiteSpace(options.Endpoint) ||
            !Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"'{AttachmentScannerOptions.SectionName}:Endpoint' must be an absolute HTTP or HTTPS URL.");
        }

        var isProduction = string.Equals(
            environmentName,
            Environments.Production,
            StringComparison.OrdinalIgnoreCase);

        if (isProduction && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"'{AttachmentScannerOptions.SectionName}:Endpoint' must use HTTPS outside Development and Testing. " +
                "Use a trusted HTTPS malware scanner endpoint in production.");
        }

        var requireScannerCredentials = !string.Equals(
            environmentName,
            Environments.Development,
            StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                environmentName,
                "Testing",
                StringComparison.OrdinalIgnoreCase);

        if (requireScannerCredentials &&
            string.IsNullOrWhiteSpace(options.ApiKey?.Trim()))
        {
            throw new InvalidOperationException(
                $"'{AttachmentScannerOptions.SectionName}:ApiKey' is required for the '{AttachmentScannerOptions.HttpProvider}' malware scanner outside Development and Testing.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"'{AttachmentScannerOptions.SectionName}:TimeoutSeconds' must be greater than zero.");
        }
    }
}
