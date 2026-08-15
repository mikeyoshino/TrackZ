using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Infrastructure.Media;
using TrackZ.Infrastructure.Persistence;

namespace TrackZ.Infrastructure.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void Object_storage_options_require_explicit_production_values()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TrackZ"] = "Host=localhost;Database=trackz_test;Username=trackz;Password=not-used",
                ["Jwt:Issuer"] = "trackz-api",
                ["Jwt:Audience"] = "trackz-mobile",
                ["Jwt:SigningKey"] = "test-signing-key-that-is-at-least-thirty-two-bytes-long",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "14"
            })
            .Build();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TrackZ.Infrastructure.Media.ObjectStorageOptions>>().Value);
    }

    [Fact]
    public void AddInfrastructure_Rejects_missing_Jwt_settings()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddInfrastructure(configuration));

        Assert.Contains("Jwt settings", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddInfrastructure_Registers_AppDbContext_As_IAppDbContext()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TrackZ"] = "Host=localhost;Database=trackz_test;Username=trackz;Password=not-used",
                ["Jwt:Issuer"] = "trackz-api",
                ["Jwt:Audience"] = "trackz-mobile",
                ["Jwt:SigningKey"] = "test-signing-key-that-is-at-least-thirty-two-bytes-long",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "14"
            })
            .Build();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<AppDbContext>(scope.ServiceProvider.GetRequiredService<IAppDbContext>());
    }

    [Fact]
    public void Api_development_configuration_resolves_AppDbContext_without_connecting()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        Assert.IsType<AppDbContext>(context);
        Assert.Equal(configuration.GetConnectionString("TrackZ"),
            ((AppDbContext)context).Database.GetDbConnection().ConnectionString);
    }

    [Fact]
    public void Api_base_configuration_does_not_embed_the_development_database_connection()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        Assert.Null(configuration.GetConnectionString("TrackZ"));
    }

    [Fact]
    public void AddInfrastructure_uses_one_object_storage_instance_and_registers_fail_fast_lifecycle_startup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TrackZ"] = "Host=localhost;Database=trackz_test;Username=trackz;Password=not-used",
                ["Jwt:Issuer"] = "trackz-api",
                ["Jwt:Audience"] = "trackz-mobile",
                ["Jwt:SigningKey"] = "test-signing-key-that-is-at-least-thirty-two-bytes-long",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "14",
                ["ObjectStorage:ServiceUrl"] = "http://127.0.0.1:9000",
                ["ObjectStorage:Bucket"] = "trackz-private",
                ["ObjectStorage:AccessKey"] = "trackz-api",
                ["ObjectStorage:SecretKey"] = "test-secret-key-not-for-production",
                ["ObjectStorage:StagingExpirationDays"] = "1"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IObjectStorage>();
        var lifecycle = provider.GetRequiredService<IStagingObjectLifecycle>();
        Assert.Same(storage, lifecycle);
        Assert.Single(provider.GetServices<IHostedService>(),
            service => service is StagingObjectLifecycleService);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void Object_storage_staging_expiration_accepts_only_one_through_thirty_days(
        int days,
        bool expected)
    {
        var options = new ObjectStorageOptions
        {
            ServiceUrl = "http://127.0.0.1:9000",
            Bucket = "trackz-private",
            AccessKey = "trackz-api",
            SecretKey = "test-secret-key-not-for-production",
            StagingExpirationDays = days
        };

        Assert.Equal(expected, options.IsValid());
    }

    [Fact]
    public async Task Staging_lifecycle_startup_propagates_configuration_failure()
    {
        var failure = new UnauthorizedAccessException("lifecycle permission denied");
        var service = new StagingObjectLifecycleService(new FailingLifecycle(failure));

        var actual = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.StartAsync(CancellationToken.None));

        Assert.Same(failure, actual);
    }

    private sealed class FailingLifecycle(Exception failure) : IStagingObjectLifecycle
    {
        public Task EnsureConfiguredAsync(CancellationToken cancellationToken) =>
            Task.FromException(failure);
    }
}
