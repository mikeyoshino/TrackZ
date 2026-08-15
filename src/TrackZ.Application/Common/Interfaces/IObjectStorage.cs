namespace TrackZ.Application.Common.Interfaces;

public sealed record ObjectStorageObject(long Length, string ContentType, Stream Content);

public interface IObjectStorage
{
    Task<ObjectStorageObject?> GetAsync(string ownerPrefix, string key, CancellationToken cancellationToken);
    Task PutAsync(string ownerPrefix, string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task DeleteAsync(string ownerPrefix, string key, CancellationToken cancellationToken);
}
