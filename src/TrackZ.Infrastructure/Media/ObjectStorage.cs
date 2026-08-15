using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;
using TrackZ.Application.Common.Interfaces;

namespace TrackZ.Infrastructure.Media;
public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";
    public string ServiceUrl { get; init; } = "http://minio:9000";
    public string Bucket { get; init; } = "trackz-private";
    public string AccessKey { get; init; } = null!;
    public string SecretKey { get; init; } = null!;
}
public sealed class ObjectStorage : IObjectStorage
{
    private readonly IAmazonS3 _s3; private readonly ObjectStorageOptions _options;
    public ObjectStorage(IOptions<ObjectStorageOptions> options)
    {
        _options = options.Value;
        _s3 = new AmazonS3Client(_options.AccessKey, _options.SecretKey, new AmazonS3Config { ServiceURL = _options.ServiceUrl, ForcePathStyle = true });
    }
    public async Task<Uri> CreateUploadUriAsync(string prefix, string key, string contentType, long length, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        Validate(prefix, key);
        return new Uri(await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest { BucketName = _options.Bucket, Key = key, Verb = HttpVerb.PUT, ContentType = contentType, Expires = expiresAt.UtcDateTime }));
    }
    public async Task<ObjectStorageObject?> GetAsync(string prefix, string key, CancellationToken cancellationToken)
    {
        Validate(prefix, key); try { var result = await _s3.GetObjectAsync(_options.Bucket, key, cancellationToken); return new ObjectStorageObject(result.ContentLength, result.Headers.ContentType, result.ResponseStream); }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }
    public Task PutAsync(string prefix, string key, Stream content, string contentType, CancellationToken cancellationToken) { Validate(prefix, key); return _s3.PutObjectAsync(new PutObjectRequest { BucketName = _options.Bucket, Key = key, InputStream = content, ContentType = contentType }, cancellationToken); }
    public Task DeleteAsync(string prefix, string key, CancellationToken cancellationToken) { Validate(prefix, key); return _s3.DeleteObjectAsync(_options.Bucket, key, cancellationToken); }
    public async Task<Uri> CreateReadUriAsync(string prefix, string key, DateTimeOffset expiresAt, CancellationToken cancellationToken) { Validate(prefix, key); return new Uri(await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest { BucketName = _options.Bucket, Key = key, Verb = HttpVerb.GET, Expires = expiresAt.UtcDateTime })); }
    private static void Validate(string prefix, string key) { if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.Contains("..", StringComparison.Ordinal)) throw new InvalidOperationException("Object key scope violation."); }
}
