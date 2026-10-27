using Avalonia.Controls;

namespace Balancia.Desktop;

/// <summary>The timeline card: cumulative totals of the current period against the previous one.</summary>
public partial class TimelineCard : UserControl
{
    public TimelineCard() => InitializeComponent();

    internal void Render(FlowChartData data) => TimelineChart.SetData(data.Points, data.Series, data.Message);
}
