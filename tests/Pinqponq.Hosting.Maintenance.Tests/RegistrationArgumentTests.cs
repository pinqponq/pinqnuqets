using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Hosting.Maintenance.DependencyInjection;
using Xunit;

namespace Pinqponq.Hosting.Maintenance.Tests;

public sealed class RegistrationArgumentTests
{
    [Fact]
    public void Null_services_are_rejected()
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddPinqponqMaintenanceMode(null!, configuration));

        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void Null_configuration_is_rejected()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddPinqponqMaintenanceMode(null!));

        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public void Null_application_builder_is_rejected()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => ((IApplicationBuilder)null!).UsePinqponqMaintenanceMode());

        Assert.Equal("app", exception.ParamName);
    }
}
