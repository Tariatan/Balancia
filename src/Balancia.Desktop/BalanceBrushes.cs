using Avalonia.Media;
using Balancia.Core;

namespace Balancia.Desktop;

/// <summary>The colors that show whether a balance is positive or not.</summary>
internal static class BalanceBrushes
{
    internal static readonly IBrush Positive = Brush.Parse("#2C8B6D");
    internal static readonly IBrush Negative = Brush.Parse("#B95D4D");

    /// <summary>Green above zero, red at zero or below.</summary>
    internal static IBrush For(Money amount) => amount > Money.Zero ? Positive : Negative;
}
