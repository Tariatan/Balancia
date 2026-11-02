using Android.Graphics;

namespace Balancia.Android;

internal static class ViewerPalette
{
    internal static Color Background { get; private set; } = Color.ParseColor("#101C24");
    internal static Color Card { get; private set; } = Color.ParseColor("#1A2934");
    internal static Color Border { get; private set; } = Color.ParseColor("#344D60");
    internal static Color Text { get; private set; } = Color.ParseColor("#D9E5FF");
    internal static Color Muted { get; private set; } = Color.ParseColor("#A9BEDF");
    internal static Color Positive { get; private set; } = Color.ParseColor("#60E9B4");
    internal static Color Negative { get; private set; } = Color.ParseColor("#FF6B69");
    internal static Color Accent { get; private set; } = Color.ParseColor("#52A5FF");
    internal static Color Selected { get; private set; } = Color.ParseColor("#1764C5");
    internal static Color Warning { get; private set; } = Color.ParseColor("#FFD16A");
    internal static void Apply(bool light)
    {
        Background = Color.ParseColor(light ? "#F6F8FA" : "#101C24");
        Card = Color.ParseColor(light ? "#FFFFFF" : "#1A2934");
        Border = Color.ParseColor(light ? "#DCE6EB" : "#344D60");
        Text = Color.ParseColor(light ? "#263C48" : "#D9E5FF");
        Muted = Color.ParseColor(light ? "#71838D" : "#A9BEDF");
        Positive = Color.ParseColor(light ? "#2C8B6D" : "#60E9B4");
        Negative = Color.ParseColor(light ? "#B95D4D" : "#FF6B69");
        Accent = Color.ParseColor(light ? "#3989A7" : "#52A5FF");
        Selected = Color.ParseColor(light ? "#D8ECF3" : "#1764C5");
        Warning = Color.ParseColor(light ? "#B58B00" : "#FFD16A");
    }
}
