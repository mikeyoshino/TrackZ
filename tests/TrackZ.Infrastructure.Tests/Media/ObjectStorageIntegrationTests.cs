using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.Extensions.Options;
using TrackZ.Infrastructure.Media;

namespace TrackZ.Infrastructure.Tests.Media;

public sealed class ObjectStorageIntegrationTests : IAsyncLifetime
{
    private const string Image = "minio/minio:RELEASE.2025-07-23T15-54-02Z";
    private const string McImage = "minio/mc:RELEASE.2025-07-21T05-28-08Z";
    private readonly string _rootAccess = "root" + Guid.NewGuid().ToString("N")[..12];
    private readonly string _rootSecret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    private readonly string _appAccess = "api" + Guid.NewGuid().ToString("N")[..12];
    private readonly string _appSecret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    private readonly string _bucket = "trackz-test-" + Guid.NewGuid().ToString("N")[..12];
    private INetwork? _network;
    private IContainer? _minio;
    private IContainer? _bootstrap;
    private string _serviceUrl = null!;

    public async Task InitializeAsync()
    {
        _network = new NetworkBuilder().Build();
        _minio = new ContainerBuilder(Image)
            .WithNetwork(_network)
            .WithNetworkAliases("minio")
            .WithEnvironment("MINIO_ROOT_USER", _rootAccess)
            .WithEnvironment("MINIO_ROOT_PASSWORD", _rootSecret)
            .WithCommand("server", "/data")
            .WithPortBinding(9000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(9000).ForPath("/minio/health/live")))
            .Build();
        await _minio.StartAsync();
        _serviceUrl = $"http://127.0.0.1:{_minio.GetMappedPublicPort(9000)}";

        var script = $"set -eu; mc alias set local http://minio:9000 {_rootAccess} {_rootSecret}; mc mb local/{_bucket}; mc anonymous set none local/{_bucket}; printf '{{\"Version\":\"2012-10-17\",\"Statement\":[{{\"Effect\":\"Allow\",\"Action\":[\"s3:GetObject\",\"s3:PutObject\",\"s3:DeleteObject\"],\"Resource\":[\"arn:aws:s3:::{_bucket}/*\"]}},{{\"Effect\":\"Allow\",\"Action\":[\"s3:GetLifecycleConfiguration\",\"s3:PutLifecycleConfiguration\"],\"Resource\":[\"arn:aws:s3:::{_bucket}\"]}}]}}' >/tmp/policy.json; mc admin policy create local trackz-api /tmp/policy.json; mc admin user add local {_appAccess} {_appSecret}; mc admin policy attach local trackz-api --user {_appAccess}; echo bootstrap-complete; tail -f /dev/null";
        _bootstrap = new ContainerBuilder(McImage)
            .WithNetwork(_network)
            .WithEntrypoint("/bin/sh")
            .WithCommand("-ec", script)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("bootstrap-complete"))
            .Build();
        await _bootstrap.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_bootstrap is not null) await _bootstrap.DisposeAsync();
        if (_minio is not null) await _minio.DisposeAsync();
        if (_network is not null) await _network.DisposeAsync();
    }

    [Fact]
    public async Task Scoped_adapter_round_trips_private_objects_and_rejects_unsafe_or_anonymous_access()
    {
        var owner = Guid.NewGuid();
        var prefix = $"private/{owner:D}/";
        var key = prefix + "exercise/thumbnail.jpg";
        var adapter = new ObjectStorage(Options.Create(new ObjectStorageOptions
        {
            ServiceUrl = _serviceUrl,
            Bucket = _bucket,
            AccessKey = _appAccess,
            SecretKey = _appSecret
        }));

        await using (var input = new MemoryStream([3, 1, 4, 1, 5]))
        {
            await adapter.PutAsync(prefix, key, input, "image/jpeg", CancellationToken.None);
        }

        var stored = await adapter.GetAsync(prefix, key, CancellationToken.None);
        Assert.NotNull(stored);
        await using (stored!.Content)
        using (var copy = new MemoryStream())
        {
            await stored.Content.CopyToAsync(copy);
            Assert.Equal([3, 1, 4, 1, 5], copy.ToArray());
        }
        Assert.Equal("image/jpeg", stored.ContentType);
        Assert.Null(await adapter.GetAsync(prefix, prefix + "missing.jpg", CancellationToken.None));

        var anonymous = await new HttpClient().GetAsync($"{_serviceUrl}/{_bucket}/{key}");
        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetAsync(prefix, prefix + "../other-owner/thumbnail.jpg", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetAsync(prefix, $"private/{Guid.NewGuid():D}/thumbnail.jpg", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.PutAsync(prefix, "private/" + owner.ToString("D") + "-sibling/file.jpg", Stream.Null, "image/jpeg", CancellationToken.None));

        await adapter.DeleteAsync(prefix, key, CancellationToken.None);
        Assert.Null(await adapter.GetAsync(prefix, key, CancellationToken.None));
        Assert.NotEqual(_rootAccess, _appAccess);
    }

    [Fact]
    public async Task Staging_lifecycle_is_exact_idempotent_and_preserves_unrelated_private_rules()
    {
        using var root = CreateClient(_rootAccess, _rootSecret);
        var unrelated = new LifecycleRule
        {
            Id = "retain-system",
            Status = LifecycleRuleStatus.Enabled,
            Filter = new LifecycleFilter
            {
                LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = "system/" }
            },
            Expiration = new LifecycleRuleExpiration { Days = 30 }
        };
        await root.PutLifecycleConfigurationAsync(new PutLifecycleConfigurationRequest
        {
            BucketName = _bucket,
            Configuration = new LifecycleConfiguration { Rules = [unrelated] }
        });
        var adapter = CreateAdapter(stagingExpirationDays: 2);

        await adapter.EnsureConfiguredAsync(CancellationToken.None);
        await adapter.EnsureConfiguredAsync(CancellationToken.None);

        var configuration = (await root.GetLifecycleConfigurationAsync(_bucket)).Configuration;
        Assert.NotNull(configuration.Rules);
        Assert.Equal(2, configuration.Rules.Count);
        var trackZ = Assert.Single(configuration.Rules, rule => rule.Id == ObjectStorage.TrackZStagingLifecycleRuleId);
        Assert.Equal(LifecycleRuleStatus.Enabled, trackZ.Status);
        Assert.Equal("staging/", PrefixOf(trackZ));
        Assert.Equal(2, trackZ.Expiration?.Days);
        var preserved = Assert.Single(configuration.Rules, rule => rule.Id == "retain-system");
        Assert.Equal("system/", PrefixOf(preserved));
        Assert.Equal(30, preserved.Expiration?.Days);

        var anonymous = await new HttpClient().GetAsync($"{_serviceUrl}/{_bucket}/staging/owner/file.png");
        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);
    }

    [Fact]
    public async Task Staging_lifecycle_fails_closed_when_the_owned_rule_conflicts()
    {
        using var root = CreateClient(_rootAccess, _rootSecret);
        await root.PutLifecycleConfigurationAsync(new PutLifecycleConfigurationRequest
        {
            BucketName = _bucket,
            Configuration = new LifecycleConfiguration
            {
                Rules =
                [
                    new LifecycleRule
                    {
                        Id = ObjectStorage.TrackZStagingLifecycleRuleId,
                        Status = LifecycleRuleStatus.Enabled,
                        Filter = new LifecycleFilter
                        {
                            LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = "private/" }
                        },
                        Expiration = new LifecycleRuleExpiration { Days = 2 }
                    }
                ]
            }
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateAdapter(stagingExpirationDays: 2).EnsureConfiguredAsync(CancellationToken.None));

        Assert.Equal("The TrackZ staging lifecycle rule conflicts with the required configuration.", exception.Message);
        var configuration = (await root.GetLifecycleConfigurationAsync(_bucket)).Configuration;
        var retained = Assert.Single(configuration.Rules!);
        Assert.Equal("private/", PrefixOf(retained));
    }

    private ObjectStorage CreateAdapter(int stagingExpirationDays = 1) => new(Options.Create(new ObjectStorageOptions
    {
        ServiceUrl = _serviceUrl,
        Bucket = _bucket,
        AccessKey = _appAccess,
        SecretKey = _appSecret,
        StagingExpirationDays = stagingExpirationDays
    }));

    private AmazonS3Client CreateClient(string accessKey, string secretKey) => new(
        accessKey,
        secretKey,
        new AmazonS3Config { ServiceURL = _serviceUrl, ForcePathStyle = true });

    private static string? PrefixOf(LifecycleRule rule) =>
        rule.Filter?.LifecycleFilterPredicate is LifecyclePrefixPredicate prefix
            ? prefix.Prefix
            : rule.Filter?.Prefix;
}
