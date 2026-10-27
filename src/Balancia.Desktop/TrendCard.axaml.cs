using Avalonia.Controls;
using Balancia.Core;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>
/// The trend card: income, expenses and savings per day, week or month, with the interval named above the chart. The mouse wheel asks for a finer or
/// coarser interval; what the interval is stays with the window.
/// </summary>
public partial class TrendCard : UserControl
{
    public TrendCard()
    {
        InitializeComponent();
        TrendPanelBorder.PointerWheelChanged += (_, args) =>
        {
            ZoomRequested?.Invoke(args.Delta.Y > 0);
            args.Handled = true;
        };
    }

    /// <summary>Raised when the wheel turns; true asks for a coarser interval, false for a finer one.</summary>
    public event Action<bool>? ZoomRequested;

    internal void Render(FlowChartData data) => TrendChart.SetData(data.Points, data.Series, data.Message);

    public void ShowInterval(FlowInterval interval) => TrendIntervalLabel.Text = Get(interval.ToString());
}
