using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VelavanDemo.Domain.Authorization;
using VelavanDemo.Domain.Entities;
using VelavanDemo.Domain.Enums;
using VelavanDemo.Infrastructure.Persistence;

namespace VelavanDemo.IntegrationTests;

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public const string TestJwtIssuer = "VelavanDemo.IntegrationTests";
    public const string TestJwtAudience = "VelavanDemo.IntegrationTests";
    public const string TestJwtSecret = "velavan-demo-integration-tests-only-secret-32-bytes-minimum";
    public const string DefaultPassword = "TestPass!2345";

    private readonly string _databaseName = $"VelavanDemo.AuthTests.{Guid.NewGuid():N}";
    private readonly string _attachmentRootPath = Path.Combine(
        Path.GetTempPath(),
        "VelavanDemo.AttachmentTests",
        Guid.NewGuid().ToString("N"));
    private bool _started;

    public string AttachmentRootPath => _attachmentRootPath;

    public AuthApiFactory()
    {
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestJwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestJwtAudience);
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "60");
        Environment.SetEnvironmentVariable("Seed__SuperAdmin__Enabled", "false");
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Server=(unused);Database=VelavanDemo.AuthTests;Trusted_Connection=True;TrustServerCertificate=True");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = TestJwtIssuer,
                ["Jwt:Audience"] = TestJwtAudience,
                ["Jwt:Secret"] = TestJwtSecret,
                ["Jwt:AccessTokenMinutes"] = "60",
                ["Seed:SuperAdmin:Enabled"] = "false",
                ["Attachments:Storage:Provider"] = "Local",
                ["Attachments:Storage:RootPath"] = _attachmentRootPath
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<DbContextOptions>();

            var optionsConfigurations = services
                .Where(descriptor =>
                    descriptor.ServiceType.IsGenericType &&
                    descriptor.ServiceType.GetGenericTypeDefinition().FullName?.Contains(
                        "DbContextOptionsConfiguration",
                        StringComparison.Ordinal) == true)
                .ToList();

            foreach (var descriptor in optionsConfigurations)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }

    public void EnsureStarted()
    {
        if (_started)
        {
            return;
        }

        using var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        _started = true;
    }

    public async Task<TestUser> CreateUserAsync(
        string roleName = RoleNames.User,
        UserStatus status = UserStatus.Active,
        string? password = null,
        string? username = null,
        string? email = null)
    {
        EnsureStarted();

        var normalizedUsername = (username ?? $"user-{Guid.NewGuid():N}")
            .Trim()
            .ToLowerInvariant();
        var normalizedEmail = (email ?? $"user-{Guid.NewGuid():N}@integration.test")
            .Trim()
            .ToLowerInvariant();
        var userPassword = password ?? DefaultPassword;

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var role = await dbContext.Roles.SingleAsync(candidate => candidate.Name == roleName);

        var user = new User
        {
            Username = normalizedUsername,
            NormalizedUsername = normalizedUsername,
            Email = normalizedEmail,
            FirstName = "Test",
            LastName = roleName.Replace(" ", string.Empty, StringComparison.Ordinal),
            Status = status
        };

        user.PasswordHash = passwordHasher.HashPassword(user, userPassword);
        user.UserRoles.Add(new UserRole { Role = role });

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return new TestUser(
            user.Id,
            user.Username,
            user.Email,
            userPassword,
            roleName);
    }

    public async Task<int> CreateProductAsync()
    {
        EnsureStarted();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var category = new Category
        {
            Name = $"Category {Guid.NewGuid():N}",
            Description = "Integration test category",
            IsActive = true
        };

        var product = new Product
        {
            Sku = $"SKU-{Guid.NewGuid():N}",
            Name = $"Product {Guid.NewGuid():N}",
            Description = "Integration test product",
            Category = category,
            UnitPrice = 100m,
            CostPrice = 60m,
            IsActive = true
        };

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        return product.Id;
    }

    public async Task<int> CreateCustomerAsync()
    {
        EnsureStarted();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var customer = new Customer
        {
            FirstName = "Integration",
            LastName = $"Customer {Guid.NewGuid():N}",
            Email = $"customer-{Guid.NewGuid():N}@integration.test",
            PhoneNumber = "+91 90000 00000",
            AddressLine1 = "1 Test Street",
            City = "Test City",
            State = "Test State",
            PostalCode = "600001",
            IsActive = true
        };

        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();

        return customer.Id;
    }

    public async Task<int> CreateOrderAsync()
    {
        var customerId = await CreateCustomerAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = new Order
        {
            OrderNumber = $"ORD-{Guid.NewGuid():N}",
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            PaymentMethod = PaymentMethod.CashOnDelivery,
            SubTotal = 100m,
            TaxAmount = 0m,
            ShippingAmount = 0m,
            TotalAmount = 100m,
            PlacedAtUtc = DateTime.UtcNow
        };

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        return order.Id;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing || !Directory.Exists(_attachmentRootPath))
        {
            return;
        }

        try
        {
            Directory.Delete(_attachmentRootPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed record TestUser(
    int Id,
    string Username,
    string? Email,
    string Password,
    string RoleName);
