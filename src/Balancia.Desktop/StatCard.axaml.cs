using Avalonia.Controls;

namespace Balancia.Desktop;

/// <summary>A titled amount with a scope caption. The amount takes the control's <c>Foreground</c>.</summary>
public partial class StatCard : UserControl
{
    public StatCard() => InitializeComponent();

    public string? Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public string? Value
    {
        get => ValueText.Text;
        set => ValueText.Text = value;
    }

    public string? Scope
    {
        get => ScopeText.Text;
        set => ScopeText.Text = value;
    }
}
