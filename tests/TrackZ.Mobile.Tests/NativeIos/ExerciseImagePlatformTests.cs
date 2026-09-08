using System.Reflection;
using System.Xml.Linq;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Features.Exercises.Services;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class ExerciseImagePlatformTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"trackz-media-picker-{Guid.NewGuid():N}");

    [Fact]
    public async Task Photo_library_picker_returns_one_privacy_safe_processed_photo()
    {
        Directory.CreateDirectory(_root);
        var photoPath = Path.Combine(_root, "equipment.jpg");
        await File.WriteAllBytesAsync(photoPath, [1, 2, 3, 4]);
        var mediaPicker = new RecordingMediaPicker
        {
            PickedPhotos = [new FileResult(photoPath, "image/jpeg")]
        };
        var constructor = typeof(MauiLocalExerciseImagePicker)
            .GetConstructor([typeof(IMediaPicker)]);

        Assert.NotNull(constructor);
        var picker = Assert.IsAssignableFrom<ILocalExerciseImagePicker>(
            constructor.Invoke([mediaPicker]));

        var selection = await picker.PickAsync("เลือกจากคลัง");

        Assert.NotNull(selection);
        Assert.Equal("equipment.jpg", selection.FileName);
        Assert.Equal("image/jpeg", selection.ReportedContentType);
        Assert.NotNull(mediaPicker.LastPickOptions);
        Assert.Equal("เลือกจากคลัง", mediaPicker.LastPickOptions.Title);
        Assert.Equal(1, mediaPicker.LastPickOptions.SelectionLimit);
        Assert.Equal(2048, mediaPicker.LastPickOptions.MaximumWidth);
        Assert.Equal(2048, mediaPicker.LastPickOptions.MaximumHeight);
        Assert.Equal(88, mediaPicker.LastPickOptions.CompressionQuality);
        Assert.True(mediaPicker.LastPickOptions.RotateImage);
        Assert.False(mediaPicker.LastPickOptions.PreserveMetaData);
    }

    [Fact]
    public async Task Camera_capture_reports_when_the_device_has_no_camera()
    {
        var mediaPicker = new RecordingMediaPicker { IsCaptureSupported = false };
        var capture = new MauiLocalExerciseImageCapture(mediaPicker);

        await Assert.ThrowsAsync<FeatureNotSupportedException>(() => capture.CaptureAsync());
    }

    [Fact]
    public void Ios_declares_camera_and_photo_library_usage_descriptions()
    {
        var document = XDocument.Load(RepoPath("src", "TrackZ.Mobile", "Platforms", "iOS", "Info.plist"));
        var entries = document.Descendants("dict").First().Elements().ToArray();

        Assert.False(string.IsNullOrWhiteSpace(PlistValue(entries, "NSCameraUsageDescription")));
        Assert.False(string.IsNullOrWhiteSpace(PlistValue(entries, "NSPhotoLibraryUsageDescription")));
        Assert.False(string.IsNullOrWhiteSpace(PlistValue(entries, "NSPhotoLibraryAddUsageDescription")));
    }

    [Fact]
    public void Image_errors_explain_the_cause_and_next_action_in_Thai()
    {
        var resolver = typeof(CustomExercisePage).GetMethod(
            "ResolveImageFailureMessage",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(resolver);
        var text = MobileResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("th-TH"));
        Assert.Equal(
            "อุปกรณ์นี้ไม่รองรับการถ่ายรูป กรุณาเลือกจากคลังแทน",
            resolver.Invoke(null, [new FeatureNotSupportedException(), text]));
        Assert.Equal(
            "TrackZ เปิดกล้องหรือคลังรูปไม่ได้ กรุณาอนุญาตการเข้าถึงในการตั้งค่า",
            resolver.Invoke(null, [new PermissionException("Photos permission denied."), text]));
        Assert.Equal(
            "นำเข้ารูปไม่สำเร็จ",
            resolver.Invoke(null, [new IOException(), text]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string? PlistValue(XElement[] entries, string key)
    {
        var keyIndex = Array.FindIndex(entries, element =>
            element.Name.LocalName == "key" && string.Equals(element.Value, key, StringComparison.Ordinal));
        return keyIndex >= 0 && keyIndex + 1 < entries.Length ? entries[keyIndex + 1].Value : null;
    }

    private static string RepoPath(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine([root, .. segments]);
    }

    private sealed class RecordingMediaPicker : IMediaPicker
    {
        public bool IsCaptureSupported { get; init; } = true;
        public List<FileResult> PickedPhotos { get; init; } = [];
        public MediaPickerOptions? LastPickOptions { get; private set; }

        [Obsolete]
        public Task<FileResult?> PickPhotoAsync(MediaPickerOptions? options = null) =>
            throw new InvalidOperationException("The single-photo legacy picker must not be used.");

        public Task<List<FileResult>> PickPhotosAsync(MediaPickerOptions? options = null)
        {
            LastPickOptions = options;
            return Task.FromResult(PickedPhotos);
        }

        public Task<FileResult?> CapturePhotoAsync(MediaPickerOptions? options = null) =>
            throw new NotSupportedException();

        [Obsolete]
        public Task<FileResult?> PickVideoAsync(MediaPickerOptions? options = null) =>
            throw new NotSupportedException();

        public Task<List<FileResult>> PickVideosAsync(MediaPickerOptions? options = null) =>
            throw new NotSupportedException();

        public Task<FileResult?> CaptureVideoAsync(MediaPickerOptions? options = null) =>
            throw new NotSupportedException();
    }
}
