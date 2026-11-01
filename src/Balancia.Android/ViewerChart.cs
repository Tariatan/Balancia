using Android.Content;
using Android.Graphics;
using Android.Views;
using Balancia.Core;
using System.Globalization;

namespace Balancia.Android;

/// <summary>Draws monetary values only after Core has calculated integer-centime totals.</summary>
internal sealed class ViewerChart : View
{
    private readonly FlowAnalytics analytics;
    private readonly IReadOnlyList<FlowBucket> buckets;
    private readonly bool timeline;
    private readonly bool expenses;
    private readonly CultureInfo displayCulture;
    private readonly Action<string, Money, Money, Money> showDetail;
    private readonly Paint paint = new(PaintFlags.AntiAlias);
    private readonly List<Money> current = [];
    private readonly List<Money> previous = [];
    private int selected = -1;
    private float plotLeft;
    private float plotRight;
    private float Density => Resources!.DisplayMetrics!.Density;

    internal ViewerChart(Context context, FlowAnalytics analytics, FlowInterval interval, bool timeline, bool expenses, CultureInfo displayCulture, Action<string, Money, Money, Money> showDetail) : base(context)
    {
        this.analytics = analytics;
        this.timeline = timeline;
        this.expenses = expenses;
        this.displayCulture = displayCulture;
        this.showDetail = showDetail;
        buckets = analytics.Trend(interval);
        if (timeline)
        {
            Fill(analytics.Current, analytics.From, current);
            if (analytics.PreviousFrom is { } start) { Fill(analytics.Previous, start, previous); }
            selected = Math.Clamp(DateOnly.FromDateTime(DateTime.Today).DayNumber - analytics.From.DayNumber, 0, current.Count - 1);
            ShowSelection();
        }
        ContentDescription = timeline ? "Cumulative current and previous period chart. Tap to inspect a day." : "Income, expenses and savings chart. Tap to inspect an interval.";
        Clickable = true;
    }

    private void Fill(IReadOnlyList<DailyFlow> days, DateOnly start, List<Money> totals)
    {
        var sum = Money.Zero;
        var index = 0;
        var length = analytics.To.DayNumber - analytics.From.DayNumber + 1;
        for (var day = 0; day < length; day++)
        {
            while (index < days.Count && days[index].Date.DayNumber - start.DayNumber <= day)
            {
                sum += expenses ? days[index].Expenses : days[index].Income;
                index++;
            }
            totals.Add(sum);
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (timeline)
        {
            DrawTimeline(canvas);
            return;
        }
        var left = 48 * Density;
        var top = 20 * Density;
        var right = Width - 10 * Density;
        var bottom = Height - 38 * Density;
        var count = timeline ? current.Count : buckets.Count;
        if (count == 0 || right <= left) { return; }
        var maximum = Math.Max(1m, timeline
            ? current.Concat(previous).Max(m => (decimal)m.Centimes)
            : buckets.Max(b => (decimal)Math.Max(b.Income.Centimes, b.Expenses.Centimes)));
        var baseline = timeline ? bottom : top + (bottom - top) * .5f;
        var span = timeline ? bottom - top : (bottom - top) * .45f;
        float Y(Money amount) => baseline - (float)(amount.Centimes / maximum) * span;
        float X(int index) => left + (right - left) * (index + .5f) / count;
        paint.TextSize = 10 * Density;
        for (var line = 0; line <= 4; line++)
        {
            var y = top + (bottom - top) * line / 4;
            paint.Color = ViewerPalette.Border;
            paint.StrokeWidth = Density;
            canvas.DrawLine(left, y, right, y, paint);
        }
        paint.Color = ViewerPalette.Muted;
        canvas.DrawText((maximum / 100m).ToString("0", CultureInfo.InvariantCulture), 0, top + 10 * Density, paint);
        canvas.DrawText("0", 22 * Density, baseline, paint);
        if (selected >= 0)
        {
            paint.Color = ViewerPalette.Selected;
            paint.Alpha = 65;
            canvas.DrawRect(X(selected) - (right - left) / count / 2, top, X(selected) + (right - left) / count / 2, bottom, paint);
            paint.Alpha = 255;
        }
        if (timeline)
        {
            DrawLine(current, expenses ? ViewerPalette.Negative : ViewerPalette.Positive, false);
            DrawLine(previous, ViewerPalette.Accent, true);
        }
        else
        {
            for (var index = 0; index < buckets.Count; index++)
            {
                var bucket = buckets[index];
                var half = Math.Max(1, (right - left) / count * .27f);
                paint.Color = ViewerPalette.Positive;
                canvas.DrawRect(X(index) - half, Y(bucket.Income), X(index) + half, baseline, paint);
                paint.Color = ViewerPalette.Negative;
                canvas.DrawRect(X(index) - half, baseline, X(index) + half, baseline + (float)(bucket.Expenses.Centimes / maximum) * span, paint);
            }
            DrawLine(buckets.Select(b => b.Savings).ToList(), ViewerPalette.Accent, false);
        }
        paint.Color = ViewerPalette.Muted;
        foreach (var index in new[] { 0, count / 2, count - 1 }.Distinct())
        {
            var date = timeline ? analytics.From.AddDays(index) : buckets[index].From;
            canvas.DrawText(date.ToString("dd MMM", displayCulture), Math.Clamp(X(index) - 16 * Density, left, right - 34 * Density), bottom + 24 * Density, paint);
        }

        void DrawLine(IReadOnlyList<Money> values, Color color, bool dashed)
        {
            paint.Color = color;
            paint.StrokeWidth = 2 * Density;
            for (var index = 1; index < values.Count; index++)
            {
                if (!dashed || index % 2 == 1) { canvas.DrawLine(X(index - 1), Y(values[index - 1]), X(index), Y(values[index]), paint); }
            }
            if (values.Count == 1) { canvas.DrawCircle(X(0), Y(values[0]), 3 * Density, paint); }
        }
    }

    private void DrawTimeline(Canvas canvas)
    {
        if (current.Count == 0) { return; }
        var highest = Math.Max(100m, current.Concat(previous).Max(value => (decimal)value.Centimes));
        var roughStep = highest / 4m;
        var magnitude = 1m;
        while (magnitude * 10m <= roughStep) { magnitude *= 10m; }
        var normalized = roughStep / magnitude;
        var step = (normalized <= 1m ? 1m : normalized <= 2m ? 2m : normalized <= 5m ? 5m : 10m) * magnitude;
        var maximum = decimal.Ceiling(highest / step) * step;
        if (maximum == highest) { maximum += step; }
        var divisions = (int)(maximum / step);
        paint.TextSize = global::Android.Util.TypedValue.ApplyDimension(global::Android.Util.ComplexUnitType.Sp, 10, Resources!.DisplayMetrics);
        string Tick(decimal centimes) => (centimes / 100m).ToString(step < 100m ? "0.##" : "N0", displayCulture);
        plotLeft = paint.MeasureText(Tick(maximum)) + 12 * Density;
        plotRight = Width - 10 * Density;
        var top = 16 * Density;
        var bottom = Height - 32 * Density;
        if (plotRight <= plotLeft || bottom <= top) { return; }
        float X(int index) => current.Count == 1 ? (plotLeft + plotRight) / 2 : plotLeft + (plotRight - plotLeft) * index / (current.Count - 1);
        float Y(Money amount) => bottom - (float)(amount.Centimes / maximum) * (bottom - top);

        for (var line = 0; line <= divisions; line++)
        {
            var value = step * line;
            var y = bottom - (float)(value / maximum) * (bottom - top);
            paint.Color = ViewerPalette.Border;
            paint.Alpha = 110;
            paint.StrokeWidth = Density;
            canvas.DrawLine(plotLeft, y, plotRight, y, paint);
            paint.Color = ViewerPalette.Muted;
            paint.Alpha = 255;
            var label = Tick(value);
            canvas.DrawText(label, plotLeft - paint.MeasureText(label) - 8 * Density, y + 3 * Density, paint);
        }
        foreach (var index in new[] { 0, (current.Count - 1) / 3, (current.Count - 1) * 2 / 3, current.Count - 1 }.Distinct())
        {
            var x = X(index);
            paint.Color = ViewerPalette.Border;
            paint.Alpha = 65;
            canvas.DrawLine(x, top, x, bottom, paint);
            paint.Color = ViewerPalette.Muted;
            paint.Alpha = 255;
            var label = analytics.From.AddDays(index).ToString("dd MMM", displayCulture);
            var width = paint.MeasureText(label);
            canvas.DrawText(label, Math.Clamp(x - width / 2, plotLeft, Math.Max(plotLeft, Width - width)), bottom + 22 * Density, paint);
        }
        var color = expenses ? ViewerPalette.Negative : ViewerPalette.Positive;
        using var linePath = BuildPath(current);
        using var fillPath = new global::Android.Graphics.Path(linePath);
        fillPath.LineTo(X(current.Count - 1), bottom);
        fillPath.LineTo(X(0), bottom);
        fillPath.Close();
        paint.Color = color;
        paint.Alpha = 22;
        paint.SetStyle(Paint.Style.Fill);
        canvas.DrawPath(fillPath, paint);
        paint.Alpha = 255;
        paint.SetStyle(Paint.Style.Stroke);
        paint.StrokeWidth = 2.5f * Density;
        paint.StrokeJoin = Paint.Join.Round;
        canvas.DrawPath(linePath, paint);
        using var previousPath = BuildPath(previous);
        using var dash = new DashPathEffect([6 * Density, 4 * Density], 0);
        paint.SetPathEffect(dash);
        paint.Color = ViewerPalette.Accent;
        paint.StrokeWidth = 2 * Density;
        canvas.DrawPath(previousPath, paint);
        paint.SetPathEffect(null);
        paint.SetStyle(Paint.Style.Fill);
        if (selected >= 0)
        {
            var x = X(selected);
            paint.Color = ViewerPalette.Muted;
            paint.Alpha = 150;
            paint.StrokeWidth = Density;
            paint.SetPathEffect(dash);
            canvas.DrawLine(x, top, x, bottom, paint);
            paint.SetPathEffect(null);
            paint.Alpha = 255;
            DrawPoint(current[selected], color);
            if (selected < previous.Count) { DrawPoint(previous[selected], ViewerPalette.Accent); }

            void DrawPoint(Money value, Color pointColor)
            {
                paint.Color = ViewerPalette.Card;
                canvas.DrawCircle(x, Y(value), 5 * Density, paint);
                paint.Color = pointColor;
                canvas.DrawCircle(x, Y(value), 3.5f * Density, paint);
            }
        }

        global::Android.Graphics.Path BuildPath(IReadOnlyList<Money> values)
        {
            var path = new global::Android.Graphics.Path();
            if (values.Count == 0) { return path; }
            path.MoveTo(X(0), Y(values[0]));
            for (var index = 1; index < values.Count; index++) { path.LineTo(X(index), Y(values[index])); }
            return path;
        }
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e?.Action != MotionEventActions.Up) { return true; }
        var count = timeline ? current.Count : buckets.Count;
        if (count == 0) { return true; }
        selected = timeline
            ? Math.Clamp((int)Math.Round((e.GetX() - plotLeft) / Math.Max(1, plotRight - plotLeft) * (count - 1)), 0, count - 1)
            : Math.Clamp((int)((e.GetX() - 48 * Density) / Math.Max(1, Width - 58 * Density) * count), 0, count - 1);
        ShowSelection();
        PerformClick();
        Invalidate();
        return true;
    }

    private void ShowSelection()
    {
        if (timeline)
        {
            var comparison = selected < previous.Count ? previous[selected] : Money.Zero;
            showDetail(analytics.From.AddDays(selected).ToString("dddd, dd MMM yyyy", displayCulture),
                current[selected], comparison, current[selected] - comparison);
        }
        else
        {
            var bucket = buckets[selected];
            showDetail($"{bucket.From.ToString("dd MMM", displayCulture)} – {bucket.To.ToString("dd MMM yyyy", displayCulture)}",
                bucket.Income, bucket.Expenses, bucket.Savings);
        }
    }

    public override bool PerformClick() => base.PerformClick();

    protected override void Dispose(bool disposing)
    {
        if (disposing) { paint.Dispose(); }
        base.Dispose(disposing);
    }
}
