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
    [Required, Url] public string ServiceUrl { get; init; } = "http://localhost:9000";
    [Required] public string Bucket { get; init; } = "trackz-private";
    [Required] public string AccessKey { get; init; } = "trackz_api";
    [Required, MinLength(16)] public string SecretKey { get; init; } = "trackz_api_development_secret";
}
public sealed class ObjectStorage : IObjectStorage
{
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
    private static void Validate(string prefix, string key) { if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.Contains("..", StringComparison.Ordinal)) throw new InvalidOperationException("Object key scope violation."); }
}
