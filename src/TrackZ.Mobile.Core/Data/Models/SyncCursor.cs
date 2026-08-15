namespace TrackZ.Mobile.Data.Models;

public sealed record SyncCursor(string Scope, string Cursor, DateTimeOffset UpdatedAt, long Version);
