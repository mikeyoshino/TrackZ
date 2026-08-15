namespace TrackZ.Contracts.Common;

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
