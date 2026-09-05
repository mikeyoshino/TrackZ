using Microsoft.Maui.Controls.Shapes;
using TrackZ.Mobile.Features.Workout;
using static TrackZ.Mobile.Features.Coach.CoachCopy;

namespace TrackZ.Mobile.Features.Coach;

internal static class CoachViews
{
    internal static View Week(CoachReport report, int? goal)
    {
        var summary = goal is > 0 ? T($"ฝึกแล้ว {report.TrainingDays} จากเป้าหมาย {goal} วัน", $"Trained {report.TrainingDays} of {goal} days")
            : T($"ฝึกแล้ว {report.TrainingDays} วัน", $"Trained {report.TrainingDays} days");
        var grid = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        var labels = Thai ? new[] { "จ.", "อ.", "พ.", "พฤ.", "ศ.", "ส.", "อา." } : new[] { "M", "T", "W", "T", "F", "S", "S" };
        var currentDate = report.Days.FirstOrDefault(day => day.IsToday)?.Date;
        for (var i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var day = report.Days[i];
            var missed = !day.Trained && currentDate is { } current && day.Date < current;
            var label = CoachUi.Label(day.Trained ? "✓" : missed ? "–" : "", 21);
            label.HorizontalTextAlignment = TextAlignment.Center;
            label.VerticalTextAlignment = TextAlignment.Center;
            if (day.Trained) label.TextColor = Color.FromArgb("#101508");
            var circle = new Border
            {
                WidthRequest = 30, HeightRequest = 30, Padding = 0, StrokeThickness = 1,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                StrokeShape = new RoundRectangle { CornerRadius = 15 },
                BackgroundColor = day.Trained ? Color.FromArgb("#C8FF3D") : missed ? Color.FromArgb("#252B31") : Colors.Transparent,
                Stroke = new SolidColorBrush(Color.FromArgb(day.Trained ? "#C8FF3D" : missed ? "#68717B" : "#78838C")), Content = label
            };
            // Today is independent of completion: keep a visible gap around
            // both a filled checkmark and an untrained outlined circle.
            var ring = new Border
            {
                WidthRequest = 40, HeightRequest = 40, Padding = 3,
                StrokeThickness = day.IsToday ? 1.5 : 0,
                HorizontalOptions = LayoutOptions.Center,
                StrokeShape = new RoundRectangle { CornerRadius = 20 },
                BackgroundColor = Colors.Transparent,
                Stroke = new SolidColorBrush(Color.FromArgb("#C8FF3D")), Content = circle
            };
            var status = day.Trained
                ? T("ฝึกแล้ว", "Trained")
                : missed
                    ? T("วันที่ผ่านมาและไม่ได้ฝึก", "Past day without a workout")
                    : T("ยังไม่ถึงวันฝึก", "Upcoming day");
            SemanticProperties.SetDescription(ring, $"{day.Date:dd MMM} {(day.IsToday ? T("วันนี้ · ", "Today · ") : "")}{status}");
            var caption = CoachUi.Label(labels[i], 12, true); caption.HorizontalTextAlignment = TextAlignment.Center;
            var column = CoachUi.Stack(ring, caption);
            column.Spacing = 4;
            if (day.IsPlanned)
            {
                var planned = CoachUi.Label(T("ตามแผน", "Planned"), 10, true);
                planned.HorizontalTextAlignment = TextAlignment.Center;
                column.Children.Add(planned);
            }
            if (day.IsToday)
            {
                var today = CoachUi.Label(T("วันนี้", "Today"), 12);
                today.HorizontalTextAlignment = TextAlignment.Center;
                today.TextColor = Color.FromArgb("#C8FF3D");
                column.Children.Add(today);
            }
            grid.Add(column, i);
        }
        return CoachUi.Stack(CoachUi.Label(summary, 15, true), grid);
    }

    internal static View? HomeAdvice(CoachReport report, IWeightUnitPreference units,
        Func<CoachExercise, Task> openAdvice, Func<CoachArea, bool, Task> recovery)
    {
        var exercise = report.Exercises.FirstOrDefault(e => e.Recommendation.Action == CoachAction.Pain);
        if (exercise is not null) return ExerciseAdviceCard(exercise, units, openAdvice);
        var area = report.Areas.FirstOrDefault(a => a.Recovery is { Ready: false } || a.NeedsCheck && a.Recovery is null);
        if (area is not null) return RecoveryCard(area, recovery);
        exercise = report.Exercises.FirstOrDefault(e => e.Recommendation.IsIncrease
            || e.Recommendation.Action is CoachAction.ChooseIncrement or CoachAction.CheckConsistency);
        if (exercise is null) return null;
        return ExerciseAdviceCard(exercise, units, openAdvice);
    }

    private static View ExerciseAdviceCard(CoachExercise exercise, IWeightUnitPreference units,
        Func<CoachExercise, Task> openAdvice)
    {
        var stack = CoachUi.Stack(CoachUi.Label(exercise.Name, 18, heading: true),
            CoachUi.Label(Title(exercise.Recommendation), 15, true));
        if (exercise.Recommendation.IsIncrease)
        {
            var target = CoachUi.Label(Target(exercise, units.Current), 21); target.TextColor = Color.FromArgb("#C8FF3D");
            stack.Children.Add(target);
            stack.Children.Add(CoachUi.Label(exercise.Accepted ? T("เป้าหมายที่คุณเลือกไว้", "Your chosen target") : T("เมื่อยังทำท่าได้ถูกต้อง", "Only while keeping your technique"), 13, true));
        }
        var link = CoachUi.Button(T("ดูเหตุผล  ›", "Why this suggestion  ›"), () => openAdvice(exercise));
        link.BackgroundColor = Colors.Transparent; link.TextColor = Color.FromArgb("#C8FF3D"); link.HorizontalOptions = LayoutOptions.End;
        stack.Children.Add(link);
        return CoachUi.Stack(CoachUi.Label(T("ครั้งหน้าลอง", "For next time"), 20, heading: true), CoachUi.Card(stack));
    }

    internal static View RecoveryCard(CoachArea area, Func<CoachArea, bool, Task> answer)
    {
        var acknowledged = area.Recovery is not null;
        var stack = CoachUi.Stack(CoachUi.Label(Body(area.BodyPart) + " · " + T(acknowledged ? "วันนี้ค่อย ๆ ฝึก" : "ยังล้าอยู่ไหม?", acknowledged ? "Take it easier" : "Still feeling tired?"), 20, heading: true),
            CoachUi.Label(acknowledged ? T("คุณบอกว่ายังล้า ลองพักส่วนนี้หรือลดเซ็ต ยังไม่ต้องเพิ่มน้ำหนัก", "You reported fatigue. Rest this area or reduce sets; do not increase load yet.")
                : T("สัปดาห์นี้ฝึกมากกว่าที่เคย เช็กความรู้สึกก่อนเพิ่มการฝึก", "This week's volume is above your usual level. Check how you feel before doing more."), 15, true));
        if (acknowledged)
            stack.Children.Add(CoachUi.Button(T("ตอนนี้รู้สึกพร้อมแล้ว", "I feel ready now"), () => answer(area, true)));
        else
        {
            stack.Children.Add(CoachUi.Button(T("พร้อม", "Ready"), () => answer(area, true)));
            stack.Children.Add(CoachUi.Button(T("ยังล้าอยู่", "Still tired"), () => answer(area, false)));
        }
        stack.Children.Add(CoachUi.Label(T("เป็นการเช็กความล้า ไม่ใช่การวินิจฉัยภาวะฝึกหนักเกินไป", "A fatigue check, not an overtraining diagnosis"), 12, true));
        return CoachUi.Card(stack);
    }

    internal static View Report(CoachReport report, int? goal, IWeightUnitPreference units,
        Func<CoachExercise, Task> openAdvice, Func<CoachArea, bool, Task> recovery)
    {
        var container = new VerticalStackLayout { Spacing = 16 };
        container.Children.Add(CoachUi.Label(T("สัปดาห์นี้", "This week"), 17, heading: true));
        var stats = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 12 };
        stats.Add(CoachUi.Card(CoachUi.Stack(CoachUi.Label(T("วันที่ฝึก", "Days trained"), 13, true),
            CoachUi.Label(goal is > 0 ? $"{report.TrainingDays} / {goal}" : $"{report.TrainingDays}", 28))));
        stats.Add(CoachUi.Card(CoachUi.Stack(CoachUi.Label(T("เซ็ตฝึก", "Working sets"), 13, true), CoachUi.Label($"{report.WorkingSets}", 28))), 1);
        container.Children.Add(stats);
        var volume = CoachUi.Stack(CoachUi.Label(T("จำนวนเซ็ตแยกตามกล้ามเนื้อ", "Sets by muscle group"), 18, heading: true));
        var max = Math.Max(1, report.Areas.Max(a => a.WorkingSets));
        foreach (var area in report.Areas)
        {
            var row = new Grid { ColumnDefinitions = [new ColumnDefinition(90), new ColumnDefinition(GridLength.Star), new ColumnDefinition(32)], ColumnSpacing = 8 };
            row.Add(CoachUi.Label(Body(area.BodyPart), 13));
            var progress = new ProgressBar { Progress = (double)area.WorkingSets / max, ProgressColor = Color.FromArgb("#C8FF3D"), VerticalOptions = LayoutOptions.Center };
            SemanticProperties.SetDescription(progress, $"{Body(area.BodyPart)} {area.WorkingSets} {T("เซ็ต", "sets")}");
            row.Add(progress, 1); var count = CoachUi.Label($"{area.WorkingSets}"); count.HorizontalTextAlignment = TextAlignment.End; row.Add(count, 2); volume.Children.Add(row);
        }
        volume.Children.Add(CoachUi.Label(T("นับกล้ามเนื้อหลักของท่า ไม่รวมวอร์มอัปและแรงช่วยจากกล้ามเนื้ออื่น", "Primary muscle group only; excludes warm-ups and indirect muscle work"), 12, true));
        if (report.UnknownSets > 0)
            volume.Children.Add(CoachUi.Label(T($"อีก {report.UnknownSets} เซ็ตยังไม่ได้ระบุประเภท จึงยังไม่รวมในกราฟ", $"{report.UnknownSets} unclassified sets are not included in this chart"), 13, true));
        container.Children.Add(CoachUi.Card(volume));
        if (!report.HasTraining && report.WorkingSets == 0)
            container.Children.Add(CoachUi.Label(T("เริ่มบันทึกการฝึก แล้วกลับมาดูความก้าวหน้าของคุณได้ที่นี่", "Log your training to see your progress here"), 15, true));
        var trend = report.Exercises.FirstOrDefault(e => ComparableTrend(e).Length >= 2);
        if (trend is not null)
        {
            var sessions = ComparableTrend(trend);
            var graph = new GraphicsView { HeightRequest = 108, Drawable = new RepTrend(sessions.Select(s => s.Reps).ToArray()) };
            var load = sessions[^1].WeightKg is { } kg ? $"{WeightUnitConversion.FromKilograms(kg, units.Current):0.##} {(units.Current == WeightDisplayUnit.Kilograms ? T("กก.", "kg") : T("ปอนด์", "lb"))}" : T("น้ำหนักตัว", "Bodyweight");
            SemanticProperties.SetDescription(graph, string.Join(", ", sessions.Select(s => $"{s.At:dd MMM}: {s.Reps} {T("ครั้ง", "reps")}")));
            container.Children.Add(CoachUi.Card(CoachUi.Stack(CoachUi.Label(trend.Name, 18, heading: true),
                CoachUi.Label(T($"น้ำหนักเดิม {load} · จำนวนครั้งต่ำสุดต่อเซ็ต", $"Same load {load} · minimum reps per set"), 13, true), graph,
                CoachUi.Label(string.Join("   ·   ", sessions.Select(s => $"{s.At.ToLocalTime():d/M}: {s.Reps}")), 12, true))));
        }
        var insight = HomeAdvice(report, units, openAdvice, recovery);
        if (insight is not null) container.Children.Add(insight);
        container.Children.Add(CoachUi.Label(T("รายงานจากข้อมูลการฝึกในเครื่อง ไม่ใช่การวัดขนาดกล้ามเนื้อ", "Based on training available on this device, not a measure of muscle growth"), 12, true));
        container.Children.Add(CoachUi.Label(LocalNotice, 12, true));
        return container;
    }

    private static CoachSession[] ComparableTrend(CoachExercise exercise)
    {
        var latest = exercise.Sessions.FirstOrDefault();
        if (latest is null || latest.HasUnknownSets || latest.Mode == TrackZ.Domain.Exercises.TrackingMode.Assisted) return [];
        return exercise.Sessions.Where(s => !s.HasUnknownSets && s.WorkingSets == latest.WorkingSets && s.WorkingSets > 0
            && s.Mode == latest.Mode && s.WeightKg == latest.WeightKg && s.AssistedKg == latest.AssistedKg)
            .Take(5).Reverse().ToArray();
    }

    private sealed class RepTrend(int[] reps) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF bounds)
        {
            if (reps.Length < 2) return;
            var min = reps.Min() - 1; var max = reps.Max() + 1;
            PointF Point(int i) => new(16 + (bounds.Width - 32) * i / (reps.Length - 1), bounds.Height - 20 - (bounds.Height - 45) * (reps[i] - min) / (max - min));
            canvas.StrokeColor = Color.FromArgb("#C8FF3D"); canvas.StrokeSize = 2;
            for (var i = 1; i < reps.Length; i++) { var a = Point(i - 1); var b = Point(i); canvas.DrawLine(a.X, a.Y, b.X, b.Y); }
            canvas.FontSize = 13;
            for (var i = 0; i < reps.Length; i++)
            {
                var p = Point(i); canvas.FillColor = Color.FromArgb("#C8FF3D"); canvas.FillCircle(p, 4);
                canvas.FontColor = Colors.White; canvas.DrawString($"{reps[i]}", p.X - 16, p.Y - 23, 32, 20, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }
}
