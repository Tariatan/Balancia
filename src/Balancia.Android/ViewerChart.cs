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
    private readonly TextView detail;
    private readonly Paint paint = new(PaintFlags.AntiAlias);
    private readonly List<Money> current = [];
    private readonly List<Money> previous = [];
    private int selected = -1;
    private float Density => Resources!.DisplayMetrics!.Density;

    internal ViewerChart(Context context, FlowAnalytics analytics, FlowInterval interval, bool timeline, bool expenses, TextView detail) : base(context)
    {
        this.analytics = analytics;
        this.timeline = timeline;
        this.expenses = expenses;
        this.detail = detail;
        buckets = analytics.Trend(interval);
        if (timeline)
        {
            Fill(analytics.Current, analytics.From, current);
            if (analytics.PreviousFrom is { } start) { Fill(analytics.Previous, start, previous); }
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
            canvas.DrawText(date.ToString("dd MMM", CultureInfo.InvariantCulture), Math.Clamp(X(index) - 16 * Density, left, right - 34 * Density), bottom + 24 * Density, paint);
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

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e?.Action != MotionEventActions.Up) { return true; }
        var count = timeline ? current.Count : buckets.Count;
        if (count == 0) { return true; }
        selected = Math.Clamp((int)((e.GetX() - 48 * Density) / Math.Max(1, Width - 58 * Density) * count), 0, count - 1);
        string Format(Money amount) => $"CHF {amount.Francs.ToString("N2", CultureInfo.GetCultureInfo("de-CH"))}";
        if (timeline)
        {
            var comparison = selected < previous.Count ? previous[selected] : Money.Zero;
            detail.Text = $"{analytics.From.AddDays(selected):dddd, dd MMM yyyy}\nCurrent   {Format(current[selected])}\nPrevious   {Format(comparison)}\nDifference   {Format(current[selected] - comparison)}";
        }
        else
        {
            var bucket = buckets[selected];
            detail.Text = $"{bucket.From:dd MMM} – {bucket.To:dd MMM yyyy}\nIncome   {Format(bucket.Income)}\nExpenses   {Format(bucket.Expenses)}\nSavings   {Format(bucket.Savings)}";
        }
        PerformClick();
        Invalidate();
        return true;
    }

    public override bool PerformClick() => base.PerformClick();

    protected override void Dispose(bool disposing)
    {
        if (disposing) { paint.Dispose(); }
        base.Dispose(disposing);
    }
}
