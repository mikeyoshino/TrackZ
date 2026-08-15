namespace TrackZ.Application.Common.Interfaces;

public sealed record ObjectStorageObject(long Length, string ContentType, Stream Content);

public interface IObjectStorage
{
    Task<Uri> CreateUploadUriAsync(string ownerPrefix, string key, string contentType, long length, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<ObjectStorageObject?> GetAsync(string ownerPrefix, string key, CancellationToken cancellationToken);
    Task PutAsync(string ownerPrefix, string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task DeleteAsync(string ownerPrefix, string key, CancellationToken cancellationToken);
    Task<Uri> CreateReadUriAsync(string ownerPrefix, string key, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}
