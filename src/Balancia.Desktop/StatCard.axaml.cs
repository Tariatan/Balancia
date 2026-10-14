using Avalonia;
using Avalonia.Controls;

namespace Balancia.Desktop;

/// <summary>A titled amount with a scope caption. The amount takes the control's <c>Foreground</c>.</summary>
public partial class StatCard : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<StatCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> ValueProperty = AvaloniaProperty.Register<StatCard, string?>(nameof(Value));

    public static readonly StyledProperty<string?> ScopeProperty = AvaloniaProperty.Register<StatCard, string?>(nameof(Scope));

    public StatCard() => InitializeComponent();

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Scope
    {
        get => GetValue(ScopeProperty);
        set => SetValue(ScopeProperty, value);
    }
}
