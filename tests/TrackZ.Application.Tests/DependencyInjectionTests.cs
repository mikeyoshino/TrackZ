using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace TrackZ.Application.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_Registers_IMediator()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        using var provider = services.BuildServiceProvider();

        Assert.IsAssignableFrom<IMediator>(provider.GetRequiredService<IMediator>());
    }
}
