using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Balancia.Core;

namespace Balancia.Desktop;

internal sealed record FlowChartSeries(string Name, string Color, bool Bars = false, bool Steps = false);
internal sealed record FlowChartPoint(double Position, string Label, string Details, IReadOnlyList<Money> Values);

internal sealed class FlowChart : Control
{
    private IReadOnlyList<FlowChartPoint> points = [];
    private IReadOnlyList<FlowChartSeries> series = [];
    private string? emptyMessage;
    private int hovered = -1;
    private static readonly IBrush AxisBrush = Brush.Parse("#CBD5DC");
    private static readonly IBrush LabelBrush = Brush.Parse("#71838D");

    public FlowChart()
    {
        Height = 170;
        ClipToBounds = true;
        PointerMoved += Hover;
        PointerExited += (_, _) =>
        {
            hovered = -1;
            ToolTip.SetIsOpen(this, false);
            InvalidateVisual();
        };
    }

    public void SetData(IReadOnlyList<FlowChartPoint> values, IReadOnlyList<FlowChartSeries> definitions, string? message)
    {
        points = values;
        series = definitions;
        emptyMessage = message;
        hovered = -1;
        ToolTip.SetTip(this, null);
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    private Rect Plot => new(52, 10, Math.Max(1, Bounds.Width - 60), Math.Max(1, Bounds.Height - 36));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, Bounds.WithX(0).WithY(0));
        var plot = Plot;
        if (emptyMessage is not null || points.Count == 0)
        {
            DrawLabel(context, emptyMessage ?? "No matching transactions", new Point(8, 70));
            return;
        }

        var values = points.SelectMany(point => point.Values).Select(value => value.Francs);
        var min = Math.Min(0m, values.Min());
        var max = Math.Max(0m, values.Max());
        if (min == max)
        {
            max = min + 1;
        }

        var step = (max - min) / 4;
        var magnitude = 1m;
        while (step >= 10)
        {
            step /= 10;
            magnitude *= 10;
        }

        while (step < 1)
        {
            step *= 10;
            magnitude /= 10;
        }

        step = Math.Max(0.01m, (step <= 1 ? 1 : step <= 2 ? 2 : step <= 5 ? 5 : 10) * magnitude);
        min = decimal.Floor(min / step) * step;
        max = decimal.Ceiling(max / step) * step;

        double Y(decimal amount) => plot.Bottom - (double)((amount - min) / (max - min)) * plot.Height;
        double X(FlowChartPoint point) => plot.Left + point.Position * plot.Width;
        for (var amount = min; amount <= max; amount += step)
        {
            var y = Y(amount);
            context.DrawLine(new Pen(AxisBrush, 0.5), new Point(plot.Left, y), new Point(plot.Right, y));
            DrawLabel(context, amount.ToString(step < 1 ? "N2" : "N0", CultureInfo.CurrentCulture), new Point(0, y - 7));
        }

        context.DrawLine(new Pen(LabelBrush, 1), new Point(plot.Left, Y(0)), new Point(plot.Right, Y(0)));
        for (var index = 0; index < series.Count; index++)
        {
            var brush = Brush.Parse(series[index].Color);
            var pen = new Pen(brush, 1.6);
            Point? previous = null;
            foreach (var point in points)
            {
                var position = new Point(X(point), Y(point.Values[index].Francs));
                if (series[index].Bars)
                {
                    var width = Math.Max(1, Math.Min(18, plot.Width / points.Count * 0.55));
                    var top = Math.Min(position.Y, Y(0));
                    var height = Math.Max(0.6, Math.Abs(position.Y - Y(0)));
                    context.DrawRectangle(brush, null, new Rect(position.X - width / 2, top, width, height));
                }
                else if (previous is { } start)
                {
                    if (series[index].Steps)
                    {
                        var corner = new Point(position.X, start.Y);
                        context.DrawLine(pen, start, corner);
                        context.DrawLine(pen, corner, position);
                    }
                    else
                    {
                        context.DrawLine(pen, start, position);
                    }
                }
                else
                {
                    context.DrawEllipse(brush, null, position, 2, 2);
                }

                previous = position;
            }
        }

        var labelCount = Math.Clamp((int)(plot.Width / 90), 2, 5);
        for (var tick = 0; tick < Math.Min(points.Count, labelCount); tick++)
        {
            var index = points.Count <= labelCount ? tick : (int)Math.Round(tick * (points.Count - 1d) / (labelCount - 1));
            var point = points[index];
            DrawLabel(context, point.Label, new Point(Math.Clamp(X(point) - 23, plot.Left, Math.Max(plot.Left, plot.Right - 57)), plot.Bottom + 8));
        }

        if (hovered >= 0 && hovered < points.Count)
        {
            var x = X(points[hovered]);
            context.DrawLine(new Pen(LabelBrush, 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
    }

    private void Hover(object? sender, PointerEventArgs args)
    {
        var position = args.GetPosition(this);
        if (points.Count == 0 || emptyMessage is not null || !Plot.Contains(position))
        {
            hovered = -1;
            ToolTip.SetIsOpen(this, false);
            InvalidateVisual();
            return;
        }

        var fraction = (position.X - Plot.Left) / Plot.Width;
        hovered = series.Any(item => item.Steps)
            ? Math.Max(0, Enumerable.Range(0, points.Count).LastOrDefault(index => points[index].Position <= fraction))
            : Enumerable.Range(0, points.Count).MinBy(index => Math.Abs(points[index].Position - fraction));
        ToolTip.SetTip(this, points[hovered].Details);
        ToolTip.SetIsOpen(this, true);
        InvalidateVisual();
    }

    private static void DrawLabel(DrawingContext context, string text, Point origin) => context.DrawText(
        new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, LabelBrush), origin);
}
