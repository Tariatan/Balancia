using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.Globalization;
using Balancia.Core;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

internal sealed record FlowChartSeries(string Name, string Color, bool Bars = false, bool Steps = false);
internal sealed record FlowChartPoint(double Position, string Label, string Details,
    IReadOnlyList<Money> Values, string? TooltipHeading = null,
    IReadOnlyList<string>? TooltipLabels = null);

internal sealed class FlowChart : Control
{
    private const int GridLines = 4;
    private static readonly IBrush AxisBrush = Brush.Parse("#CBD5DC");
    private static readonly IBrush LabelBrush = Brush.Parse("#71838D");
    private static readonly Pen GridPen = new(AxisBrush, 0.5);
    private static readonly Pen LabelPen = new(LabelBrush);
    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly CultureInfo AmountCulture = CultureInfo.GetCultureInfo("de-CH");

    private IReadOnlyList<FlowChartPoint> points = [];
    private IReadOnlyList<FlowChartSeries> series = [];
    private IBrush[] seriesBrushes = [];
    private string? emptyMessage;
    private int hovered = -1;

    public FlowChart()
    {
        Height = 170;
        ClipToBounds = true;
        PointerMoved += Hover;
        PointerExited += (_, _) => ResetHover();
    }

    public void SetData(IReadOnlyList<FlowChartPoint> values, IReadOnlyList<FlowChartSeries> definitions, string? message)
    {
        points = values;
        series = definitions;
        seriesBrushes = definitions.Select(item => Brush.Parse(item.Color)).ToArray();
        emptyMessage = message;
        ToolTip.SetTip(this, null);
        ResetHover();
    }

    private bool IsEmpty => emptyMessage is not null || points.Count == 0;

    private Rect Plot => new(52, 10, Math.Max(1, Bounds.Width - 60), Math.Max(1, Bounds.Height - 36));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, Bounds.WithX(0).WithY(0));
        if (IsEmpty)
        {
            DrawLabel(context, emptyMessage ?? Get("No matching transactions"), new Point(8, 70));
            return;
        }

        var plot = Plot;
        var (min, max, step) = ComputeScale();
        double Y(decimal amount) => plot.Bottom - (double)((amount - min) / (max - min)) * plot.Height;
        double X(FlowChartPoint point) => plot.Left + point.Position * plot.Width;

        DrawGrid(context, plot, min, max, step, Y);
        DrawSeries(context, plot, X, Y);
        DrawXLabels(context, plot, X);

        if (hovered >= 0 && hovered < points.Count)
        {
            var x = X(points[hovered]);
            context.DrawLine(LabelPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
    }

    private (decimal Min, decimal Max, decimal Step) ComputeScale()
    {
        var min = 0m;
        var max = 0m;
        foreach (var point in points)
        {
            foreach (var value in point.Values)
            {
                min = Math.Min(min, value.Francs);
                max = Math.Max(max, value.Francs);
            }
        }

        if (min == max)
        {
            max = min + 1;
        }

        var step = (max - min) / GridLines;
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
        return (decimal.Floor(min / step) * step, decimal.Ceiling(max / step) * step, step);
    }

    private static void DrawGrid(DrawingContext context, Rect plot, decimal min, decimal max, decimal step, Func<decimal, double> y)
    {
        var format = step < 1 ? "N2" : "N0";
        for (var amount = min; amount <= max; amount += step)
        {
            var line = y(amount);
            context.DrawLine(GridPen, new Point(plot.Left, line), new Point(plot.Right, line));
            DrawLabel(context, amount.ToString(format, Culture), new Point(0, line - 7));
        }

        context.DrawLine(LabelPen, new Point(plot.Left, y(0)), new Point(plot.Right, y(0)));
    }

    private void DrawSeries(DrawingContext context, Rect plot, Func<FlowChartPoint, double> x, Func<decimal, double> y)
    {
        var zeroY = y(0);
        var barWidth = Math.Max(1, Math.Min(18, plot.Width / points.Count * 0.55));
        for (var index = 0; index < series.Count; index++)
        {
            var definition = series[index];
            var brush = seriesBrushes[index];
            var pen = new Pen(brush, 1.6);
            Point? previous = null;
            foreach (var point in points)
            {
                var position = new Point(x(point), y(point.Values[index].Francs));
                if (definition.Bars)
                {
                    var top = Math.Min(position.Y, zeroY);
                    var height = Math.Max(0.6, Math.Abs(position.Y - zeroY));
                    context.DrawRectangle(brush, null, new Rect(position.X - barWidth / 2, top, barWidth, height));
                }
                else if (previous is { } start)
                {
                    if (definition.Steps)
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
    }

    private void DrawXLabels(DrawingContext context, Rect plot, Func<FlowChartPoint, double> x)
    {
        var labelCount = Math.Clamp((int)(plot.Width / 90), 2, 5);
        for (var tick = 0; tick < Math.Min(points.Count, labelCount); tick++)
        {
            var index = points.Count <= labelCount ? tick : (int)Math.Round(tick * (points.Count - 1d) / (labelCount - 1));
            var point = points[index];
            DrawLabel(context, point.Label, new Point(Math.Clamp(x(point) - 23, plot.Left, Math.Max(plot.Left, plot.Right - 57)), plot.Bottom + 8));
        }
    }

    private void ResetHover()
    {
        hovered = -1;
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    private void Hover(object? sender, PointerEventArgs args)
    {
        var plot = Plot;
        var position = args.GetPosition(this);
        if (IsEmpty || !plot.Contains(position))
        {
            ResetHover();
            return;
        }

        var previousHovered = hovered;
        hovered = FindNearestPoint((position.X - plot.Left) / plot.Width);
        if (previousHovered != hovered)
        {
            ToolTip.SetTip(this, BuildTooltip(points[hovered]));
        }

        ToolTip.SetIsOpen(this, true);
        InvalidateVisual();
    }

    private int FindNearestPoint(double fraction)
    {
        var stepped = series.Any(item => item.Steps);
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < points.Count; index++)
        {
            var offset = points[index].Position - fraction;
            if (stepped)
            {
                if (offset <= 0)
                {
                    best = index;
                }
            }
            else if (Math.Abs(offset) < bestDistance)
            {
                bestDistance = Math.Abs(offset);
                best = index;
            }
        }

        return best;
    }

    private object BuildTooltip(FlowChartPoint point)
    {
        if (point.TooltipHeading is null)
        {
            return point.Details;
        }

        var rowCount = Math.Min(point.TooltipLabels?.Count ?? series.Count, series.Count);
        var rows = new List<ChartTooltipRow>(rowCount);
        for (var index = 0; index < rowCount; index++)
        {
            rows.Add(new ChartTooltipRow(
                point.TooltipLabels?[index] ?? series[index].Name,
                point.Values[index].Francs.ToString("N2", AmountCulture),
                seriesBrushes[index]));
        }

        return new ChartTooltipData(point.TooltipHeading, rows);
    }

    private static void DrawLabel(DrawingContext context, string text, Point origin) => context.DrawText(
        new FormattedText(text, Culture, FlowDirection.LeftToRight, LabelTypeface, 10, LabelBrush), origin);
}
