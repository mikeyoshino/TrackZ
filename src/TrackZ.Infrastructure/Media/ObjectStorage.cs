using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using TrackZ.Application.Common.Interfaces;

namespace TrackZ.Infrastructure.Media;
public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";
    [Required] public string? ServiceUrl { get; init; }
    [Required] public string? Bucket { get; init; }
    [Required] public string? AccessKey { get; init; }
    [Required, MinLength(8)] public string? SecretKey { get; init; }
    [Range(1, 30)] public int StagingExpirationDays { get; init; } = 1;

    public bool IsValid() =>
        Uri.TryCreate(ServiceUrl, UriKind.Absolute, out var serviceUrl)
        && (string.Equals(serviceUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(serviceUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrWhiteSpace(serviceUrl.Host)
        && Bucket is { Length: >= 3 and <= 63 } bucket
        && bucket.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-')
        && !bucket.StartsWith(".", StringComparison.Ordinal)
        && !bucket.EndsWith(".", StringComparison.Ordinal)
        && !bucket.Contains("..", StringComparison.Ordinal)
        && AccessKey is { Length: >= 3 }
        && SecretKey is { Length: >= 8 }
        && StagingExpirationDays is >= 1 and <= 30;
}
public interface IStagingObjectLifecycle
{
    Task EnsureConfiguredAsync(CancellationToken cancellationToken);
}

public sealed class ObjectStorage : IObjectStorage, IStagingObjectLifecycle
{
    public const string TrackZStagingLifecycleRuleId = "trackz-staging-expiration-v1";
    private const string StagingPrefix = "staging/";
    private readonly IAmazonS3 _s3; private readonly ObjectStorageOptions _options;
    public ObjectStorage(IOptions<ObjectStorageOptions> options)
    {
        _options = options.Value;
        _s3 = new AmazonS3Client(_options.AccessKey, _options.SecretKey, new AmazonS3Config { ServiceURL = _options.ServiceUrl, ForcePathStyle = true });
    }
    public async Task<ObjectStorageObject?> GetAsync(string prefix, string key, CancellationToken cancellationToken)
    {
        Validate(prefix, key); try { var result = await _s3.GetObjectAsync(_options.Bucket, key, cancellationToken); return new ObjectStorageObject(result.ContentLength, result.Headers.ContentType, result.ResponseStream); }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }
    public Task PutAsync(string prefix, string key, Stream content, string contentType, CancellationToken cancellationToken) { Validate(prefix, key); return _s3.PutObjectAsync(new PutObjectRequest { BucketName = _options.Bucket, Key = key, InputStream = content, ContentType = contentType }, cancellationToken); }
    public Task DeleteAsync(string prefix, string key, CancellationToken cancellationToken) { Validate(prefix, key); return _s3.DeleteObjectAsync(_options.Bucket, key, cancellationToken); }

    public async Task EnsureConfiguredAsync(CancellationToken cancellationToken)
    {
        var configuration = await GetLifecycleConfigurationAsync(cancellationToken);
        var rules = configuration.Rules?.ToList() ?? [];
        var ownedRules = rules
            .Where(rule => string.Equals(rule.Id, TrackZStagingLifecycleRuleId, StringComparison.Ordinal))
            .ToList();

        if (ownedRules.Count > 0)
        {
            if (ownedRules.Count != 1 || !IsRequiredStagingRule(ownedRules[0]))
                throw new InvalidOperationException("The TrackZ staging lifecycle rule conflicts with the required configuration.");
            return;
        }

        rules.Add(CreateRequiredStagingRule());
        await _s3.PutLifecycleConfigurationAsync(new PutLifecycleConfigurationRequest
        {
            BucketName = _options.Bucket,
            Configuration = new LifecycleConfiguration { Rules = rules }
        }, cancellationToken);

        var installed = await GetLifecycleConfigurationAsync(cancellationToken);
        var installedOwnedRules = installed.Rules?
            .Where(rule => string.Equals(rule.Id, TrackZStagingLifecycleRuleId, StringComparison.Ordinal))
            .ToList() ?? [];
        if (installedOwnedRules.Count != 1 || !IsRequiredStagingRule(installedOwnedRules[0]))
            throw new InvalidOperationException("The TrackZ staging lifecycle rule could not be verified.");
    }

    private async Task<LifecycleConfiguration> GetLifecycleConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await _s3.GetLifecycleConfigurationAsync(_options.Bucket, cancellationToken)).Configuration
                ?? new LifecycleConfiguration();
        }
        catch (AmazonS3Exception exception) when (
            exception.StatusCode == System.Net.HttpStatusCode.NotFound
            || string.Equals(exception.ErrorCode, "NoSuchLifecycleConfiguration", StringComparison.Ordinal))
        {
            return new LifecycleConfiguration();
        }
    }

    private LifecycleRule CreateRequiredStagingRule() => new()
    {
        Id = TrackZStagingLifecycleRuleId,
        Status = LifecycleRuleStatus.Enabled,
        Filter = new LifecycleFilter
        {
            LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = StagingPrefix }
        },
        Expiration = new LifecycleRuleExpiration { Days = _options.StagingExpirationDays }
    };

    private bool IsRequiredStagingRule(LifecycleRule rule) =>
        rule.Status == LifecycleRuleStatus.Enabled
        && rule.Filter?.LifecycleFilterPredicate is LifecyclePrefixPredicate prefix
        && string.Equals(prefix.Prefix, StagingPrefix, StringComparison.Ordinal)
        && rule.Expiration is { Days: var days }
        && days == _options.StagingExpirationDays
        && rule.Expiration.Date is null
        && rule.Expiration.ExpiredObjectDeleteMarker != true
        && rule.AbortIncompleteMultipartUpload is null
        && rule.NoncurrentVersionExpiration is null
        && (rule.NoncurrentVersionTransitions is null || rule.NoncurrentVersionTransitions.Count == 0)
        && (rule.Transitions is null || rule.Transitions.Count == 0);

    private static void Validate(string prefix, string key)
    {
        if (string.IsNullOrWhiteSpace(prefix)
            || !prefix.EndsWith("/", StringComparison.Ordinal)
            || prefix.Contains("..", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(key)
            || !key.StartsWith(prefix, StringComparison.Ordinal)
            || key.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Object key scope violation.");
        }
    }
}
