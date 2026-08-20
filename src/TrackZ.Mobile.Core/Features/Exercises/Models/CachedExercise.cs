using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;

namespace TrackZ.Mobile.Features.Exercises.Models;

public sealed class CachedExercise : INotifyPropertyChanged
{
    private bool _isSelected;

    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required BodyPart BodyPart { get; init; }
    public required TrackingMode TrackingMode { get; init; }
    public string? ThumbnailUri { get; init; }
    public string? RemoteThumbnailRoute { get; init; }
    public Guid? LibraryImageId { get; init; }
    public DateTimeOffset? LastPerformedAt { get; init; }
    public PerformanceSetDto? LastBestSet { get; init; }
    public PerformanceSetDto? AllTimeBest { get; init; }
    public bool IsCustom { get; init; }
    public bool IsPendingSync { get; init; }
    public DateTimeOffset LastSyncedAt { get; init; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal static CachedExercise FromDto(
        ExerciseSummaryDto source,
        DateTimeOffset lastSyncedAt,
        string? localThumbnailUri = null) => new()
    {
        Id = source.Id,
        Name = source.Name,
        BodyPart = source.BodyPart,
        TrackingMode = source.TrackingMode,
        ThumbnailUri = localThumbnailUri,
        RemoteThumbnailRoute = source.ThumbnailUrl,
        LibraryImageId = source.LibraryImageId,
        LastPerformedAt = source.LastPerformedAt,
        LastBestSet = source.LastBestSet,
        AllTimeBest = source.AllTimeBest,
        IsCustom = source.IsCustom,
        LastSyncedAt = lastSyncedAt
    };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
