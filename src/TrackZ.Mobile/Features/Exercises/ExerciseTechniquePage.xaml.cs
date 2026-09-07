using System.Globalization;
using System.Windows.Input;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Exercises;

public partial class ExerciseTechniquePage : ContentPage, IQueryAttributable
{
    private readonly ExerciseCache _cache;
    private readonly IExerciseThumbnailCache _thumbnailCache;
    private readonly ExerciseTechniqueCatalog _catalog;
    private readonly ExercisePickerViewModel _picker;
    private readonly WorkoutTextSet _text;
    private Guid _exerciseId;
    private bool _loaded;
    private double _imageScale = 1;
    private double _startTranslationX;
    private double _startTranslationY;

    public ExerciseTechniquePage(
        ExerciseCache cache,
        IExerciseThumbnailCache thumbnailCache,
        ExerciseTechniqueCatalog catalog,
        ExercisePickerViewModel picker,
        WorkoutTextSet text)
    {
        _cache = cache;
        _thumbnailCache = thumbnailCache;
        _catalog = catalog;
        _picker = picker;
        _text = text;
        AddCommand = new AsyncCommand(_ => AddExerciseAsync());
        InitializeComponent();
        BindingContext = this;
    }

    public ICommand AddCommand { get; }
    public string PageTitle => IsThai ? "วิธีฝึก" : "How to do it";
    public string ExerciseName { get; private set; } = string.Empty;
    public string ExerciseMeta { get; private set; } = string.Empty;
    public string? ImageUri { get; private set; }
    public bool HasArtwork => !string.IsNullOrWhiteSpace(ImageUri);
    public bool ShowsArtworkPlaceholder => !HasArtwork;
    public string StepsTitle => IsThai ? "ทำตาม 3 ขั้นตอนนี้" : "Follow these 3 steps";
    public string Step1 { get; private set; } = string.Empty;
    public string Step2 { get; private set; } = string.Empty;
    public string Step3 { get; private set; } = string.Empty;
    public string TipTitle => IsThai ? "เคล็ดลับเล็ก ๆ" : "Quick tip";
    public string Tip { get; private set; } = string.Empty;
    public string CautionText { get; private set; } = string.Empty;
    public string AddButtonText { get; private set; } = string.Empty;
    public string ImageTitle => IsThai ? "ดูภาพท่าฝึก" : "Exercise image";
    public string ZoomHelp => IsThai ? "กางสองนิ้วหรือแตะสองครั้งเพื่อขยาย" : "Pinch or double-tap to zoom";

    private bool IsThai => _text.BodyPartChest == "หน้าอก";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("exerciseId", out var raw)
            || !Guid.TryParse(Uri.UnescapeDataString(Convert.ToString(raw) ?? string.Empty), out _exerciseId))
            _exerciseId = Guid.Empty;
        _loaded = false;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded || _exerciseId == Guid.Empty) return;
        _loaded = true;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var exercise = await _cache.FindExerciseAsync(_exerciseId);
        if (exercise is null) return;

        ExerciseName = exercise.Name;
        ExerciseMeta = $"{BodyPartLabel(exercise.BodyPart)} · {ExerciseEquipmentLabels.Identify(exercise).Label(IsThai)}";
        ImageUri = exercise.ThumbnailUri;
        if (string.IsNullOrWhiteSpace(ImageUri) && !string.IsNullOrWhiteSpace(exercise.RemoteThumbnailRoute))
        {
            try { ImageUri = await _thumbnailCache.CacheAsync(exercise.RemoteThumbnailRoute); }
            catch { ImageUri = null; }
        }

        var guidance = _catalog.Get(exercise.Name, exercise.BodyPart, exercise.IsCustom, CultureInfo.CurrentUICulture);
        Step1 = guidance.Steps[0];
        Step2 = guidance.Steps[1];
        Step3 = guidance.Steps[2];
        Tip = guidance.Tip;
        CautionText = (IsThai ? "ระวัง: " : "Watch out: ") + guidance.Caution;
        AddButtonText = _picker.SelectedExerciseIds.Contains(_exerciseId)
            ? (IsThai ? "เลือกท่านี้แล้ว" : "Already selected")
            : (IsThai ? "+ เพิ่มท่านี้" : "+ Add this exercise");
        NotifyAll();
    }

    private async Task AddExerciseAsync()
    {
        if (_exerciseId == Guid.Empty) return;
        if (!_picker.SelectedExerciseIds.Contains(_exerciseId))
            _picker.ToggleSelectionCommand.Execute(_exerciseId);
        await Shell.Current.GoToAsync("..");
    }

    private string BodyPartLabel(BodyPart bodyPart) => bodyPart switch
    {
        BodyPart.Chest => IsThai ? "หน้าอก" : "Chest",
        BodyPart.Back => IsThai ? "หลัง" : "Back",
        BodyPart.Shoulders => IsThai ? "ไหล่" : "Shoulders",
        BodyPart.Arms => IsThai ? "แขน" : "Arms",
        BodyPart.Legs => IsThai ? "ขา" : "Legs",
        BodyPart.Core => IsThai ? "แกนกลางลำตัว" : "Core",
        _ => string.Empty
    };

    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

    private void OnOpenImageTapped(object? sender, TappedEventArgs e)
    {
        if (!HasArtwork) return;
        ResetImage();
        ImageViewer.IsVisible = true;
    }

    private void OnCloseImageClicked(object? sender, EventArgs e)
    {
        ImageViewer.IsVisible = false;
        ResetImage();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!ImageViewer.IsVisible) return base.OnBackButtonPressed();
        ImageViewer.IsVisible = false;
        ResetImage();
        return true;
    }

    private void OnViewerPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started) _imageScale = ViewerImage.Scale;
        if (e.Status == GestureStatus.Running)
            ViewerImage.Scale = Math.Clamp(_imageScale * e.Scale, 1, 3);
        if (e.Status == GestureStatus.Completed && ViewerImage.Scale <= 1.01) ResetImage();
    }

    private void OnViewerPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (ViewerImage.Scale <= 1) return;
        if (e.StatusType == GestureStatus.Started)
        {
            _startTranslationX = ViewerImage.TranslationX;
            _startTranslationY = ViewerImage.TranslationY;
        }
        else if (e.StatusType == GestureStatus.Running)
        {
            ViewerImage.TranslationX = _startTranslationX + e.TotalX;
            ViewerImage.TranslationY = _startTranslationY + e.TotalY;
        }
    }

    private void OnViewerDoubleTapped(object? sender, TappedEventArgs e)
    {
        ViewerImage.Scale = ViewerImage.Scale > 1 ? 1 : 2;
        if (ViewerImage.Scale == 1) ResetImage();
    }

    private void ResetImage()
    {
        ViewerImage.Scale = 1;
        ViewerImage.TranslationX = 0;
        ViewerImage.TranslationY = 0;
        _imageScale = 1;
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(ExerciseName));
        OnPropertyChanged(nameof(ExerciseMeta));
        OnPropertyChanged(nameof(ImageUri));
        OnPropertyChanged(nameof(HasArtwork));
        OnPropertyChanged(nameof(ShowsArtworkPlaceholder));
        OnPropertyChanged(nameof(Step1));
        OnPropertyChanged(nameof(Step2));
        OnPropertyChanged(nameof(Step3));
        OnPropertyChanged(nameof(Tip));
        OnPropertyChanged(nameof(CautionText));
        OnPropertyChanged(nameof(AddButtonText));
    }
}
