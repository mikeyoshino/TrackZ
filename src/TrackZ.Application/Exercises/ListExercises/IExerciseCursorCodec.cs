namespace TrackZ.Application.Exercises.ListExercises;

public interface IExerciseCursorCodec
{
    CatalogCursor Decode(string cursor);

    string Encode(CatalogCursor cursor);
}

public sealed record CatalogCursor(int Version, string OrderingName, Guid OrderingId);
