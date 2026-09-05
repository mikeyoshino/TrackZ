using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using static TrackZ.Mobile.Features.Coach.CoachCopy;

namespace TrackZ.Mobile.Features.Coach;

public sealed class ExerciseCheckInPage : ContentPage
{
    private readonly TrainingCoachSource _source;
    private readonly CoachJournal _journal;
    private readonly IAccountSessionBoundary _boundary;
    private readonly IWeightUnitPreference _units;
    private readonly IExerciseGuidancePreferenceStore _increments;
    private readonly IClock _clock;
    private readonly Guid _exerciseId;
    private readonly AccountSessionGeneration _generation;
    private readonly VerticalStackLayout _body = new() { Spacing = 16, Padding = new Thickness(20, 12, 20, 24) };
    private readonly Label _error = CoachUi.Label("", 14);
    private CancellationTokenSource? _lifetime;
    private LocalWorkout? _workout;
    private LocalWorkoutExercise? _exercise;
    private CachedExercise? _definition;
    private CoachJournalData _data = CoachJournalData.Empty();
    private CoachAssessment? _assessment;
    private CoachExercise? _advice;
    private int _step;
    private int _effort;
    private bool? _controlled;
    private bool _pain;
    private bool _saving;
    private int _finishing;

    public ExerciseCheckInPage(TrainingCoachSource source, CoachJournal journal, IAccountSessionBoundary boundary,
        IWeightUnitPreference units, IExerciseGuidancePreferenceStore increments, IClock clock, Guid exerciseId)
    {
        _source = source; _journal = journal; _boundary = boundary; _units = units; _increments = increments;
        _clock = clock; _exerciseId = exerciseId; _generation = boundary.Capture();
        SetDynamicResource(BackgroundColorProperty, "TrackZBackground");
        Shell.SetNavBarIsVisible(this, false); Shell.SetTabBarIsVisible(this, false);
        SafeAreaEdges = SafeAreaEdges.All;
        _error.TextColor = Color.FromArgb("#F87171");
        Content = new ScrollView { Content = _body };
        RenderLoading();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        EnsureLifetime();
        await LoadContextAsync();
    }

    private void EnsureLifetime()
    {
        if (_lifetime is not null) return;
        _lifetime = new CancellationTokenSource();
        _boundary.SessionReset += OnReset;
    }

    internal async Task LoadContextAsync()
    {
        EnsureLifetime();
        if (_saving) return;
        _workout = null; _exercise = null; _definition = null;
        _data = CoachJournalData.Empty(); _assessment = null; _advice = null;
        _step = 0; _effort = 0; _controlled = null; _pain = false;
        RenderLoading();
        await RunAsync(async token =>
        {
            var context = await _source.GetActiveExerciseAsync(_exerciseId, token);
            var data = await _journal.ReadAsync(token);
            token.ThrowIfCancellationRequested();
            if (context is null) throw new InvalidOperationException();
            (_workout, _exercise, _definition) = context.Value;
            _data = data;
            _assessment = data.Assessments.LastOrDefault(a => a.WorkoutId == _workout.Id && a.ExerciseId == _exerciseId);
            // A completed check-in is shown, not asked again; new/edited sets invalidate it.
            var sets = LiveSets();
            if (_assessment is { } assessment && sets.Length > 0 && assessment.LastSetId == sets[^1].Id
                && assessment.At >= sets.Max(s => s.UpdatedAt ?? s.CompletedAt))
            {
                _effort = assessment.Effort; _controlled = assessment.Controlled; _pain = assessment.Pain;
                await LoadAdviceAsync(token); _step = 2;
            }
            Render();
        }, disableForm: false, RenderLoadError);
    }

    protected override void OnDisappearing()
    {
        _boundary.SessionReset -= OnReset;
        _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null;
        base.OnDisappearing();
    }

    private void OnReset(object? sender, EventArgs args)
    {
        _lifetime?.Cancel(); _workout = null; _exercise = null; _definition = null;
        _data = CoachJournalData.Empty(); _advice = null; _assessment = null;
        MainThread.BeginInvokeOnMainThread(() => _body.Children.Clear());
    }

    private LocalSet[] LiveSets() => _exercise?.Sets.Where(s => s.DeletedAt is null).OrderBy(s => s.Order).ToArray() ?? [];

    private void AddHeader(string titleText)
    {
        var header = new Grid { ColumnDefinitions = [new ColumnDefinition(52), new ColumnDefinition(GridLength.Star), new ColumnDefinition(52)] };
        header.Add(CoachUi.Button("‹", BackAsync));
        var title = CoachUi.Label(titleText, 18, heading: true);
        title.HorizontalTextAlignment = TextAlignment.Center;
        header.Add(title, 1);
        _body.Children.Add(header);
    }

    private void RenderLoading()
    {
        _body.Children.Clear();
        AddHeader(T("เช็กหลังจบท่า", "Exercise check-in"));
        _body.Children.Add(new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#C8FF3D") });
        _body.Children.Add(CoachUi.Label(T("กำลังโหลดข้อมูลเซ็ต…", "Loading your sets…"), 15, true));
        _body.Children.Add(CoachUi.Button(T("ข้ามและกลับไปฝึก", "Skip and return to workout"), FinishAsync));
    }

    private void RenderLoadError()
    {
        _body.Children.Clear();
        AddHeader(T("เช็กหลังจบท่า", "Exercise check-in"));
        _error.Text = T("โหลดข้อมูลท่านี้ไม่ได้ อาจไม่มีอยู่ในการฝึกปัจจุบันแล้ว", "Could not load this exercise. It may no longer be in the active workout.");
        _body.Children.Add(_error);
        _body.Children.Add(CoachUi.Button(T("ลองอีกครั้ง", "Try again"), LoadContextAsync, true));
        _body.Children.Add(CoachUi.Button(T("ข้ามและกลับไปฝึก", "Skip and return to workout"), FinishAsync));
        _body.Children.Add(CoachUi.Label(LocalNotice, 12, true));
    }

    private void Render()
    {
        _body.Children.Clear();
        AddHeader(_step == 0 ? T("จบท่านี้", "Finish exercise") : _step == 1 ? T("เช็กการทำท่า", "Check your technique") : T("ครั้งหน้าลองแบบนี้", "For your next session"));
        if (_definition is null) return;
        _body.Children.Add(CoachUi.Card(CoachUi.Stack(CoachUi.Label(_definition.Name, 20, heading: true), CoachUi.Label(Body(_definition.BodyPart), 14, true))));
        if (_step == 0) RenderEffort();
        else if (_step == 1) RenderControl();
        else RenderRecommendation();
        _body.Children.Add(_error);
        _body.Children.Add(CoachUi.Label(LocalNotice, 12, true));
    }

    private void RenderEffort()
    {
        var sets = LiveSets();
        _body.Children.Add(CoachUi.Label(T("เซ็ตวันนี้", "Today's sets"), 19, heading: true));
        foreach (var set in sets)
        {
            var known = _data.Warmups.TryGetValue(set.Id, out var warmup);
            var type = !known ? T("ระบุประเภท", "Set type") : warmup ? T("วอร์มอัป", "Warm-up") : T("เซ็ตฝึก", "Working set");
            var display = set.WeightKg is { } kg ? $"{WeightUnitConversion.FromKilograms(kg, _units.Current):0.##} {(_units.Current == WeightDisplayUnit.Kilograms ? T("กก.", "kg") : T("ปอนด์", "lb"))} × {set.Reps}" : $"{set.Reps} {T("ครั้ง", "reps")}";
            var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 8 };
            row.Add(CoachUi.Label($"#{set.Order + 1}   {display}"));
            row.Add(CoachUi.Button(type, () => RunAsync(async token =>
            {
                var next = known ? !warmup : false;
                await CommitForCurrentExerciseAsync(ct => _journal.SetWarmupAsync(set.Id, next, ct), token);
                _data.Warmups[set.Id] = next; Render();
            })), 1);
            _body.Children.Add(CoachUi.Card(row));
        }
        if (sets.Any(s => !_data.Warmups.ContainsKey(s.Id)))
        {
            _body.Children.Add(CoachUi.Button(T("เซ็ตที่ยังไม่ระบุเป็นเซ็ตฝึกทั้งหมด", "Mark remaining sets as working sets"), () => RunAsync(async token =>
            {
                foreach (var set in sets.Where(s => !_data.Warmups.ContainsKey(s.Id)))
                {
                    await CommitForCurrentExerciseAsync(ct => _journal.SetWarmupAsync(set.Id, false, ct), token);
                    _data.Warmups[set.Id] = false;
                }
                Render();
            })));
        }
        _body.Children.Add(CoachUi.Label(T("เซ็ตสุดท้ายรู้สึกอย่างไร?", "How did the last working set feel?"), 20, heading: true));
        _body.Children.Add(CoachUi.Label(T("ตอบครั้งเดียวหลังจบท่า · ข้ามได้", "Once per exercise · optional"), 13, true));
        var options = new[] { T("ยังทำต่อได้อีกหลายครั้ง", "Several reps left"), T("ทำต่อได้อีกนิด", "A few reps left"), T("ทำต่อไม่ไหวแล้ว", "No more reps left") };
        for (var i = 0; i < options.Length; i++)
        {
            var value = i + 1;
            var button = CoachUi.Button((_effort == value ? "●  " : "○  ") + options[i], () => { _effort = value; Render(); return Task.CompletedTask; });
            if (_effort == value) { button.BorderColor = Color.FromArgb("#C8FF3D"); button.BorderWidth = 1; }
            SemanticProperties.SetDescription(button, options[i] + (_effort == value ? T(" เลือกแล้ว", " selected") : ""));
            _body.Children.Add(button);
        }
        _body.Children.Add(CoachUi.Button(T("มีอาการเจ็บ", "I have pain"), () => { _pain = true; _controlled = null; return SaveAndRecommendAsync(); }));
        var next = CoachUi.Button(T("บันทึกและไปต่อ", "Save and continue"), async () =>
        {
            if (_effort == 1) { _step = 1; Render(); }
            else await SaveAndRecommendAsync();
        }, true);
        next.IsEnabled = _effort != 0 && sets.Length > 0; next.Opacity = next.IsEnabled ? 1 : 0.45;
        _body.Children.Add(next);
        _body.Children.Add(CoachUi.Button(T("ข้ามและกลับไปฝึก", "Skip and return to workout"), FinishAsync));
    }

    private void RenderControl()
    {
        _body.Children.Add(CoachUi.Label(T("ตอนท้ายยังทำท่าได้เหมือนตอนเริ่มไหม?", "Could you keep the same technique at the end?"), 22, heading: true));
        _body.Children.Add(CoachUi.Label(T("ไม่เหวี่ยงตัวช่วยยก และยังเคลื่อนไหวได้ตามท่าที่ฝึก", "No swinging to assist, with the intended movement for this exercise"), 15, true));
        foreach (var choice in new[] { true, false })
        {
            var label = choice ? T("ได้ ไม่ต้องเหวี่ยงตัวช่วย", "Yes, controlled without swinging") : T("เริ่มต้องฝืนหรือเหวี่ยงตัว", "I started straining or swinging");
            _body.Children.Add(CoachUi.Button((_controlled == choice ? "●  " : "○  ") + label, () => { _controlled = choice; Render(); return Task.CompletedTask; }));
        }
        var button = CoachUi.Button(T("ดูเป้าหมายครั้งหน้า", "See next-session target"), SaveAndRecommendAsync, true);
        button.IsEnabled = _controlled is not null; button.Opacity = button.IsEnabled ? 1 : .45; _body.Children.Add(button);
        _body.Children.Add(CoachUi.Label(T("คุณเป็นผู้ประเมินท่า แอปไม่ได้ตรวจฟอร์มจากภาพ", "Technique is self-reported, not verified by the app"), 13, true));
        _body.Children.Add(CoachUi.Button(T("ข้ามและกลับไปฝึก", "Skip and return to workout"), FinishAsync));
    }

    private void RenderRecommendation()
    {
        var recommendation = _advice?.Recommendation ?? new CoachRecommendation(_pain ? CoachAction.Pain : CoachAction.Hold);
        _body.Children.Add(CoachUi.Card(CoachUi.Stack(
            CoachUi.Label(CoachCopy.Title(recommendation), 22, heading: true),
            CoachUi.Label(_advice is null ? "" : Target(_advice, _units.Current), 24),
            CoachUi.Label(T("ทำไมแนะนำแบบนี้?", "Why this suggestion?"), 16, heading: true),
            CoachUi.Label(Reason(recommendation), 15, true)), recommendation.IsIncrease));
        if (recommendation.Action == CoachAction.ChooseIncrement)
        {
            var entry = new Entry { Keyboard = Keyboard.Numeric, Placeholder = _units.Current == WeightDisplayUnit.Kilograms ? T("กก.", "kg") : T("ปอนด์", "lb"), FontFamily = "NotoSansThaiRegular", TextColor = Colors.White };
            _body.Children.Add(CoachUi.Label(T("ระบุน้ำหนักที่อุปกรณ์เพิ่มได้ทีละน้อยที่สุด", "Enter the smallest increase your equipment allows"), 14, true));
            _body.Children.Add(CoachUi.Card(entry));
            _body.Children.Add(CoachUi.Button(T("ดูคำแนะนำ", "Show suggestion"), () => RunAsync(async token =>
            {
                if (!decimal.TryParse(entry.Text, out var value) || value <= 0) throw new ArgumentException();
                await CommitAsync(_ => { _increments.SetIncrementKg(_exerciseId, WeightUnitConversion.ToKilograms(value, _units.Current)); return Task.CompletedTask; }, token);
                await LoadAdviceAsync(token); Render();
            })));
        }
        if (recommendation.IsIncrease)
        {
            _body.Children.Add(CoachUi.Label(T("ถ้าเริ่มทำท่าได้ไม่เหมือนเดิม ยังไม่ต้องเพิ่มนะ", "If your technique changes, do not increase yet"), 14, true));
            _body.Children.Add(CoachUi.Button(T("ใช้เป้าหมายนี้", "Use this target"), () => AcceptAsync(true), true));
            _body.Children.Add(CoachUi.Button(T("คงเดิม", "Keep my current target"), () => AcceptAsync(false)));
        }
        else _body.Children.Add(CoachUi.Button(T("กลับไปหน้าการฝึก", "Return to workout"), FinishAsync, true));
        _body.Children.Add(CoachUi.Button(T("แก้คำตอบ", "Edit check-in"), () => { _step = 0; _pain = false; _controlled = null; Render(); return Task.CompletedTask; }));
    }

    private Task SaveAndRecommendAsync() => RunAsync(async token =>
    {
        if (_workout is null || LiveSets().Length == 0) return;
        _assessment = new CoachAssessment(_workout.Id, _exerciseId, _clock.UtcNow, _effort, _controlled, _pain, false, LiveSets()[^1].Id);
        await CommitForCurrentExerciseAsync(ct => _journal.SaveAssessmentAsync(_assessment, ct), token);
        await LoadAdviceAsync(token); _step = 2; Render();
    });
    private async Task LoadAdviceAsync(CancellationToken token) => _advice = (await _source.LoadAsync(token)).Exercises.FirstOrDefault(e => e.Id == _exerciseId);
    private Task AcceptAsync(bool accepted) => RunAsync(async token =>
    {
        if (_assessment is null) return;
        await CommitForCurrentExerciseAsync(ct => _journal.SaveAssessmentAsync(_assessment with { Accepted = accepted }, ct), token);
        await FinishAsync();
    });
    private async Task EnsureCurrentContextAsync(CancellationToken token)
    {
        var expectedWorkout = _workout?.Id;
        var expectedExercise = _exercise?.Id;
        var expectedSets = LiveSets();
        var context = await _source.GetActiveExerciseAsync(_exerciseId, token);
        if (context is null || context.Value.Workout.Id != expectedWorkout || context.Value.Exercise.Id != expectedExercise)
        {
            _workout = null; _exercise = null; _definition = null;
            _assessment = null; _advice = null; _step = 0; _effort = 0; _controlled = null; _pain = false;
            RenderLoadError();
            throw new ExerciseContextChangedException();
        }
        var currentSets = context.Value.Exercise.Sets
            .Where(set => set.DeletedAt is null)
            .OrderBy(set => set.Order)
            .ToArray();
        (_workout, _exercise, _definition) = context.Value;
        if (!expectedSets.SequenceEqual(currentSets))
        {
            _data = await _journal.ReadAsync(token);
            _assessment = null; _advice = null; _step = 0; _effort = 0; _controlled = null; _pain = false;
            _error.Text = T("ข้อมูลเซ็ตเปลี่ยนแล้ว โปรดตรวจสอบและตอบใหม่", "Your sets changed. Review them and answer again.");
            Render();
            throw new ExerciseContextChangedException();
        }
    }
    private async Task CommitAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        if (!await _boundary.TryCommitAsync(_generation, action, token)) throw new OperationCanceledException();
    }
    private Task CommitForCurrentExerciseAsync(Func<CancellationToken, Task> action, CancellationToken token) =>
        CommitAsync(async commitToken =>
        {
            await EnsureCurrentContextAsync(commitToken);
            await action(commitToken);
        }, token);
    private async Task RunAsync(Func<CancellationToken, Task> action, bool disableForm = true, Action? onError = null)
    {
        if (_saving || _lifetime is null || _boundary.IsCancellationRequested(_generation)) return;
        _saving = true; _error.Text = "";
        if (disableForm) _body.IsEnabled = false;
        try
        {
            using var lease = _boundary.CreateCancellationLease(_generation, _lifetime.Token);
            await action(lease.Token);
        }
        catch (OperationCanceledException) { }
        catch (ExerciseContextChangedException) { }
        catch (Exception)
        {
            if (onError is not null) onError();
            else
            {
                _error.Text = T("ยังบันทึกไม่ได้ ลองอีกครั้ง ข้อมูลเซ็ตยังอยู่ครบ", "Could not save. Try again; your logged sets are safe.");
                if (!_body.Children.Contains(_error)) _body.Children.Add(_error);
            }
        }
        finally
        {
            _saving = false;
            if (disableForm) _body.IsEnabled = true;
        }
    }
    private Task BackAsync()
    {
        if (_step > 0) { _step--; Render(); return Task.CompletedTask; }
        return Navigation.PopAsync(false);
    }
    private async Task FinishAsync()
    {
        if (_boundary.IsCancellationRequested(_generation) || Interlocked.CompareExchange(ref _finishing, 1, 0) != 0) return;
        try
        {
            var navigation = Navigation;
            await navigation.PopAsync(false);
            if (navigation.NavigationStack.LastOrDefault() is SetLoggerPage) await navigation.PopAsync(false);
        }
        finally { Interlocked.Exchange(ref _finishing, 0); }
    }

    private sealed class ExerciseContextChangedException : Exception { }
}
