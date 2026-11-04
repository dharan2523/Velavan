using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VelavanDemo.Infrastructure;
using VelavanDemo.Infrastructure.Seed;

namespace VelavanDemo.IntegrationTests;

public sealed class SuperAdminSeedOptionsTests
{
    [Fact]
    public void AddInfrastructure_BindsSuperAdminSectionFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=(unused);Database=ConfigurationTests",
                ["Seed:SuperAdmin:Enabled"] = "true",
                ["Seed:SuperAdmin:Username"] = "configuration-test-admin",
                ["Seed:SuperAdmin:Email"] = "configuration-test@integration.test",
                ["Seed:SuperAdmin:FirstName"] = "Configuration",
                ["Seed:SuperAdmin:LastName"] = "Test",
                ["Seed:SuperAdmin:Password"] = "ConfigurationTest!2345"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SuperAdminSeedOptions>>().Value;

        Assert.True(options.Enabled);
        Assert.Equal("configuration-test-admin", options.Username);
        Assert.Equal("configuration-test@integration.test", options.Email);
        Assert.Equal("Configuration", options.FirstName);
        Assert.Equal("Test", options.LastName);
        Assert.Equal("ConfigurationTest!2345", options.Password);
    }
}
