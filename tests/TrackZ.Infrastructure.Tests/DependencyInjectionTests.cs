using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TrackZ.Infrastructure.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddInfrastructure_Returns_The_Provided_Service_Collection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var returnedServices = services.AddInfrastructure(configuration);

        Assert.Same(services, returnedServices);
    }
}
