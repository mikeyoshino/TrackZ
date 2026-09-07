using Microsoft.Maui.Controls.Shapes;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Coach;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;
using static TrackZ.Mobile.Features.Coach.CoachCopy;
using static TrackZ.Mobile.Features.Progress.ProgressPresentation;

namespace TrackZ.Mobile.Features.Progress;

internal static class MuscleProgressViews
{
    internal static View Overview(MuscleProgressReport report, Func<BodyPart, Task> open)
    {
        var result = Stack(14);
        var summary = report.ImprovedAreas > 0
            ? T($"{report.ImprovedAreas} ส่วนมีท่าที่ทำได้ดีขึ้น", $"Improvement in {report.ImprovedAreas} muscle groups")
            : report.Areas.All(a => a.Exercises.Count == 0) ? T("เริ่มเห็นความก้าวหน้าจากการบันทึก", "Your progress starts with a workout")
            : T("มาดูผลการฝึกแต่ละส่วนกัน", "See how each muscle group is doing");
        var header = new Grid { ColumnDefinitions = [new(40), new(GridLength.Star)], ColumnSpacing = 12 };
        header.Add(new Image { Source = "progress_trend.png", WidthRequest = 36, HeightRequest = 36 });
        header.Add(Stack(3, Label(summary, 17, heading: true),
            Label(T("เทียบเฉพาะท่าเดิมที่มีข้อมูลเปรียบเทียบ", "Comparing the same exercises with usable data"), 12, true)), 1);
        result.Add(Card(header));
        result.Add(Label(T("แยกตามส่วนที่ฝึก", "By muscle group"), 16, heading: true));
        var list = Stack(6);
        foreach (var area in report.Areas)
        {
            if (LargeText)
            {
                var content = Stack(4, Label(BodyName(area.BodyPart), 14, heading: true), Label(AreaStatus(area), 12), Label(AreaHint(area), 11, true));
                content.Padding = 12;
                list.Add(LinkCard(content, $"{BodyName(area.BodyPart)}, {AreaStatus(area)}", () => open(area.BodyPart)));
                continue;
            }
            var row = new Grid { Padding = new Thickness(10, 10), ColumnSpacing = 10,
                ColumnDefinitions = [new(30), new(92), new(GridLength.Star), new(14)] };
            row.Add(new Image { Source = BodyImage(area.BodyPart), WidthRequest = 28, HeightRequest = 36 });
            row.Add(Label(BodyName(area.BodyPart), 14, heading: true), 1);
            var status = new Grid { ColumnDefinitions = [new(18), new(GridLength.Star)], ColumnSpacing = 4 };
            status.Add(new Image { Source = area.Improved > 0 ? "progress_up.png" : area.Comparable > 0 ? "progress_equal.png" : "progress_pending.png",
                WidthRequest = 16, HeightRequest = 16 });
            status.Add(Label(AreaStatus(area), 12), 1);
            row.Add(Stack(2, status, Label(AreaHint(area), 11, true)), 2);
            row.Add(new Image { Source = "chevron_right.png", WidthRequest = 12, HeightRequest = 18 }, 3);
            list.Add(LinkCard(row, $"{BodyName(area.BodyPart)}, {AreaStatus(area)}. {AreaHint(area)}", () => open(area.BodyPart)));
        }
        result.Add(list);
        var note = Label(T("แสดงผลการฝึก ไม่ใช่การวัดขนาดกล้ามเนื้อ", "Training performance, not a measure of muscle size"), 12, true);
        note.HorizontalTextAlignment = TextAlignment.Center;
        result.Add(note);
        return result;
    }

    internal static View Area(MuscleProgressArea area, WeightDisplayUnit unit, Func<Guid, Task> open)
    {
        var summary = area.Improved > 0 ? T($"{area.Improved} จาก {area.Exercises.Count} ท่าทำได้ดีขึ้น", $"Improved in {area.Improved} of {area.Exercises.Count} exercises")
            : AreaStatus(area);
        var rest = area.Exercises.Count - area.Improved;
        var subtitle = rest > 0 && area.Improved > 0
            ? T($"ดูอีก {rest} ท่าและรายละเอียดด้านล่าง", $"See the other {rest} exercises below") : AreaHint(area);
        var header = new Grid { ColumnDefinitions = [new(40), new(GridLength.Star)], ColumnSpacing = 12 };
        header.Add(new Image { Source = "progress_trend.png", WidthRequest = 36, HeightRequest = 36 });
        header.Add(Stack(3, Label(summary, 17, heading: true), Label(subtitle, 12, true)), 1);
        var result = Stack(10, Card(header), Label(T("ท่าที่คุณฝึก", "Your exercises"), 15, heading: true));
        foreach (var exercise in area.Exercises)
        {
            var content = Stack(1, Label(exercise.Exercise.Name, 15),
                exercise.Improved ? Accent(Change(exercise), 12) : Label(Change(exercise), 12, true));
            if (exercise.Before is { } before && exercise.Latest is { } latest)
            {
                content.Add(Columns(Stack(1, Label(T("ก่อน", "Before"), 11, true), Label(Performance(before, unit), 14)),
                    Stack(1, Label(T("ล่าสุด", "Latest"), 11, true), Label(Performance(latest, unit), 14))));
                content.Add(Label(ComparisonNote(exercise), 11, true));
            }
            else content.Add(Label(T("บันทึกท่าเดิมด้วยจำนวนเซ็ตและน้ำหนักที่เทียบกันได้", "Log the same exercise with comparable sets and load"), 12, true));
            var row = new Grid { Padding = 10, ColumnDefinitions = [new(GridLength.Star), new(14)], ColumnSpacing = 8 };
            row.Add(content); row.Add(new Image { Source = "chevron_right.png", WidthRequest = 12, HeightRequest = 18 }, 1);
            result.Add(LinkCard(row, exercise.Exercise.Name + ", " + Change(exercise), () => open(exercise.Exercise.Id)));
        }
        if (area.Exercises.Count == 0)
            result.Add(Card(Stack(6, Label(T("ยังไม่มีบันทึกในช่วงนี้", "No sessions in this period"), 16),
                Label(T("ลองเลือกช่วงเวลาอื่น หรือเริ่มบันทึกการฝึก", "Choose another period, or log a workout"), 13, true))));
        var weeks = new Grid { ColumnSpacing = 6 };
        var max = Math.Max(1, area.Weeks.Max(w => w.Sets));
        for (var i = 0; i < area.Weeks.Count; i++)
        {
            var week = area.Weeks[i];
            weeks.ColumnDefinitions.Add(new(area.Weeks.Count > 6 ? new GridLength(42) : GridLength.Star));
            var value = Label(week.Sets.ToString(), 12); value.HorizontalTextAlignment = TextAlignment.Center;
            var bar = new Border { BackgroundColor = Color.FromArgb("#68717B"), StrokeThickness = 0,
                HeightRequest = Math.Max(2, 32d * week.Sets / max), WidthRequest = area.Weeks.Count > 6 ? 12 : 26,
                StrokeShape = new RoundRectangle { CornerRadius = 3 }, VerticalOptions = LayoutOptions.End, HorizontalOptions = LayoutOptions.Center };
            var slot = new Grid { HeightRequest = 32 }; slot.Add(bar);
            var label = Label($"{i + 1}", 10, true); label.HorizontalTextAlignment = TextAlignment.Center;
            var column = Stack(2, value, slot, label);
            SemanticProperties.SetDescription(column, $"{Date(week.Start)} – {Date(week.End)}: {week.Sets} {T("เซ็ต", "sets")}");
            weeks.Add(column, i);
        }
        View weeklyChart = area.Weeks.Count > 6
            ? new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = weeks, HorizontalScrollBarVisibility = ScrollBarVisibility.Always }
            : weeks;
        var volume = Stack(4, Label(T("ฝึกมากแค่ไหน", "How much you trained"), 16, heading: true),
            Label(T("เซ็ตต่อสัปดาห์ · ดูแยกจากผลการฝึก", "Sets per week · separate from performance"), 11, true), weeklyChart);
        var unknown = area.Weeks.Sum(w => w.UnclassifiedSets);
        if (unknown > 0) volume.Add(Label(T($"อีก {unknown} เซ็ตยังไม่ระบุว่าเป็นวอร์มอัป จึงยังไม่รวม", $"{unknown} unclassified sets are not included"), 12, true));
        result.Add(Card(volume, new Thickness(12)));
        result.Add(Label(T("นับเฉพาะเซ็ตฝึกของกล้ามเนื้อหลัก", "Working sets for primary muscles only"), 11, true));
        return result;
    }

    internal static string ComparisonNote(ExerciseProgressEvidence e) =>
        (e.Change is ProgressChange.MoreWeight or ProgressChange.LessAssistance ? T("จำนวนครั้งไม่น้อยลง", "Reps maintained or increased")
        : T("น้ำหนักเดิม", "Same load")) + T($" · {e.Latest!.WorkingSets} เซ็ตเท่ากัน", $" · Same {e.Latest!.WorkingSets} sets");

    internal static View Detail(ExerciseProgressEvidence e, WeightDisplayUnit unit, Func<bool, Task> checkIn, Func<Task> history)
    {
        var meta = BodyName(e.Exercise.BodyPart) + (e.Exercise.Equipment == ExerciseEquipment.Other ? "" : " · " + e.Exercise.Equipment.Label(Thai));
        var content = Stack(12, Stack(2, Label(e.Exercise.Name, 20, heading: true), Label(meta, 13, true)),
            e.Improved ? Accent((e.Change == ProgressChange.MoreReps ? T("น้ำหนักเดิม ", "Same load, ") : "") + Change(e), 16)
                : Label(Change(e), 16));
        if (e.Before is { } before && e.Latest is { } latest)
        {
            var sameLoad = e.Change is not (ProgressChange.MoreWeight or ProgressChange.LessAssistance);
            var first = Label(sameLoad ? $"{before.Reps}" : Load(before, unit), sameLoad ? 36 : 23);
            var last = Label(sameLoad ? $"{latest.Reps}" : Load(latest, unit), sameLoad ? 36 : 23);
            var beforeLabel = Label(T("ก่อน", "Before"), 12, true);
            var latestLabel = Label(T("ล่าสุด", "Latest"), 12, true);
            beforeLabel.HorizontalTextAlignment = latestLabel.HorizontalTextAlignment = first.HorizontalTextAlignment = last.HorizontalTextAlignment = TextAlignment.Center;
            var comparison = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            if (LargeText)
            {
                comparison.RowDefinitions.Add(new(GridLength.Auto)); comparison.RowDefinitions.Add(new(GridLength.Auto));
                comparison.Add(Stack(2, beforeLabel, first)); comparison.Add(Stack(2, latestLabel, last), 0, 1);
            }
            else
            {
                comparison.ColumnDefinitions.Add(new(GridLength.Star)); comparison.ColumnDefinitions.Add(new(32)); comparison.ColumnDefinitions.Add(new(GridLength.Star));
                comparison.Add(Stack(2, beforeLabel, first));
                comparison.Add(new Image { Source = "progress_compare.png", WidthRequest = 26, HeightRequest = 22 }, 1);
                comparison.Add(Stack(2, latestLabel, last), 2);
            }
            content.Add(Card(Stack(6, comparison,
                Label(sameLoad ? $"{Load(latest, unit)} · {latest.WorkingSets} {T("เซ็ตเท่ากัน · ครั้งต่อเซ็ต", "equal sets · reps per set")}" : ComparisonNote(e), 12, true))));
        }
        if (e.Trend.Count >= 4)
        {
            var chart = new GraphicsView { HeightRequest = 128, Drawable = new RepsChart(e.Trend) };
            SemanticProperties.SetDescription(chart, string.Join("; ", e.Trend.Select(s => $"{Date(s)}: {s.Reps} {T("ครั้ง", "reps")}")));
            content.Add(Card(Stack(6, Label(T("จำนวนครั้งที่ทำได้", "Reps over time"), 16, heading: true),
                Label(T("ครั้ง / เซ็ต", "Reps / set"), 11, true), chart,
                Label(T("เซ็ตที่ทำได้น้อยที่สุด", "Minimum reps across sets") + " · " + Load(e.Latest!, unit), 12, true),
                Label(T("แสดงเฉพาะวันที่เปรียบเทียบกันได้", "Only comparable sessions are shown"), 11, true))));
        }
        else content.Add(Card(Stack(6, Label(T("แนวโน้มการฝึก", "Training trend"), 16, heading: true),
            Label(T($"มีข้อมูลที่เทียบกันได้ {e.Trend.Count} ครั้ง", $"{e.Trend.Count} comparable sessions available"), 14),
            Label(T("เมื่อมีครบ 4 ครั้ง จะเริ่มแสดงกราฟให้ดู", "A chart appears after 4 comparable sessions"), 12, true))));
        if (e.Latest is { LastSetId: var setId } session && setId != Guid.Empty)
        {
            var check = Stack(6, Label(T("ก่อนลองเพิ่มครั้ง", "Before trying more reps"), 16, heading: true),
                Label(T("ครั้งล่าสุดในช่วงนี้ ยังควบคุมท่าได้ดีไหม?", "Could you control your form in this period's latest session?"), 12, true),
                Columns(Button(T("ยังทำท่าได้ดี", "Good control"), () => checkIn(true), session.Controlled == true),
                    Button(T("เริ่มเสียท่า", "Losing form"), () => checkIn(false), session.Controlled == false)));
            if (session.Controlled is not null) check.Add(Label(T("บันทึกคำตอบแล้ว", "Check-in saved"), 12, true));
            // No increase is authorized by a form-only answer. Existing coaching checks still apply.
            if (session.Controlled == false) check.Add(Label(T("ครั้งหน้าคงระดับที่ยังควบคุมท่าได้", "Next time, keep a level you can control"), 13, true));
            content.Add(Card(check));
        }
        var link = Button(T($"ดูบันทึกทั้ง {e.Sessions.Count} ครั้ง  ›", $"View all {e.Sessions.Count} sessions  ›"), history);
        link.BackgroundColor = Colors.Transparent; link.BorderWidth = 0; link.TextColor = Color.FromArgb("#C8FF3D");
        content.Add(link);
        content.Add(Label(T("ประเมินจากบันทึกและความรู้สึกของคุณ", "Based on your records and check-ins"), 12, true));
        return content;
    }

    private sealed class RepsChart(IReadOnlyList<CoachSession> sessions) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF bounds)
        {
            var min = sessions.Min(s => s.Reps); var max = sessions.Max(s => s.Reps);
            if (max == min) { min = Math.Max(0, min - 1); max++; }
            var left = 24f; var right = bounds.Width - 14; var top = 20f; var bottom = bounds.Height - 30;
            var start = sessions[0].At.ToUnixTimeSeconds(); var duration = Math.Max(1, sessions[^1].At.ToUnixTimeSeconds() - start);
            PointF Point(CoachSession s) => new(left + (right - left) * (s.At.ToUnixTimeSeconds() - start) / duration,
                bottom - (bottom - top) * (s.Reps - min) / (max - min));
            canvas.FontSize = 11;
            foreach (var value in new[] { min, (min + max) / 2, max }.Distinct())
            {
                var y = bottom - (bottom - top) * (value - min) / (max - min);
                canvas.StrokeColor = Color.FromArgb("#3A424B"); canvas.StrokeSize = 1; canvas.DrawLine(left, y, right, y);
                canvas.FontColor = Color.FromArgb("#A7AFB8"); canvas.DrawString(value.ToString(), 0, y - 8, 20, 16, HorizontalAlignment.Left, VerticalAlignment.Center);
            }
            canvas.StrokeColor = Color.FromArgb("#C8FF3D"); canvas.StrokeSize = 2;
            for (var i = 1; i < sessions.Count; i++) canvas.DrawLine(Point(sessions[i - 1]), Point(sessions[i]));
            for (var i = 0; i < sessions.Count; i++)
            {
                var point = Point(sessions[i]); canvas.FillColor = Color.FromArgb("#15191D");
                canvas.FillCircle(point, 4); canvas.DrawCircle(point, 4);
                if (sessions.Count > 5 && i != 0 && i != sessions.Count - 1) continue;
                canvas.FontColor = Colors.White;
                canvas.DrawString(sessions[i].Reps.ToString(), point.X - 15, point.Y - 21, 30, 18, HorizontalAlignment.Center, VerticalAlignment.Center);
                canvas.FontColor = Color.FromArgb("#A7AFB8");
                var x = Math.Clamp(point.X - 20, 0, bounds.Width - 40);
                canvas.DrawString(sessions[i].At.ToLocalTime().ToString("d/M"), x, bottom + 8, 40, 20, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }
}
