using Balancia.Core;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private void InitializeTrendPanel()
    {
        RefreshTrendIntervalLabel();
        TrendCard.ZoomRequested += zoomOut =>
        {
            if (overviewState.ZoomTrend(zoomOut))
            {
                RefreshTrendIntervalLabel();
                UpdateAnalyticsCharts();
            }
        };
    }

    private void RefreshTrendIntervalLabel() => TrendCard.ShowInterval(overviewState.TrendInterval);

    private void UpdateAnalyticsCharts()
    {
        if (overview?.Analytics is not { } data)
        {
            return;
        }

        var filter = overview.Query.Filter;
        var focus = FlowChartModel.Focus(filter, data);
        var empty = FlowChartModel.EmptyMessage(filter, data);
        TrendCard.Render(FlowChartModel.Trend(data, overviewState.TrendInterval, focus, empty));
        TimelineCard.Render(FlowChartModel.Timeline(data, focus == FlowFocus.Income, empty));
    }
}
