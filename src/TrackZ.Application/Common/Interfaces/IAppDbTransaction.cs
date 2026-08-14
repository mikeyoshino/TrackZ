namespace TrackZ.Application.Common.Interfaces;

public interface IAppDbTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
