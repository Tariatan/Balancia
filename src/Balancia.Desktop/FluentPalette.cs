using Avalonia.Controls;

namespace Balancia.Desktop;

/// <summary>Connects Fluent control states to the same loadable palette used by the ledger and dialogs.</summary>
internal static class FluentPalette
{
    internal static void Register(IResourceDictionary resources)
    {
        // Fluent templates look these up directly, including in popups and focused/hovered controls.
        foreach (var state in (string[])[string.Empty, "PointerOver", "Pressed", "Focused", "Unfocused", "FocusedPressed", "Disabled"])
        {
            var foreground = state == "Disabled" ? Palette.TextMuted : Palette.Text;
            var background = state switch
            {
                "PointerOver" => Palette.AccentSurfaceHover,
                "Pressed" => Palette.AccentSurfacePressed,
                _ => Palette.Surface,
            };
            foreach (var prefix in (string[])["TextControl", "ComboBox", "CalendarDatePicker", "ComboBoxItem", "CalendarView"])
            {
                resources[prefix + "Foreground" + state] = foreground;
                resources[prefix + "Background" + state] = background;
                resources[prefix + "BorderBrush" + state] = state == "Focused" ? Palette.Accent : Palette.Border;
            }

            foreach (var prefix in (string[])["Button", "TextControlButton", "CalendarViewNavigationButton"])
            {
                resources[prefix + "Foreground" + state] = foreground;
                resources[prefix + "Background" + state] = state switch
                {
                    "PointerOver" => Palette.AccentSurfaceHover,
                    "Pressed" => Palette.AccentSurfacePressed,
                    _ => Palette.AccentSurface,
                };
                resources[prefix + "BorderBrush" + state] = Palette.Border;
            }

            resources["ComboBoxDropDownGlyphForeground" + state] = foreground;
            resources["CalendarDatePickerCalendarGlyphForeground" + state] = foreground;
            foreach (var checkState in (string[])["Unchecked", "Checked", "Indeterminate"])
            {
                resources["CheckBoxForeground" + checkState + state] = foreground;
                resources["CheckBoxCheckBackgroundStroke" + checkState + state] = foreground;
                resources["CheckBoxCheckBackgroundFill" + checkState + state] = Palette.Surface;
                resources["CheckBoxCheckGlyphForeground" + checkState + state] = foreground;
            }
        }

        resources["TextControlPlaceholderForeground"] = Palette.TextMuted;
        resources["ComboBoxPlaceHolderForeground"] = Palette.TextMuted;
        resources["ComboBoxPlaceHolderForegroundFocusedPressed"] = Palette.TextMuted;
        resources["ComboBoxDropDownBackground"] = Palette.PopupBackground;
        resources["ComboBoxDropDownBorderBrush"] = Palette.PopupBorder;
        resources["ComboBoxBackgroundBorderBrushFocused"] = Palette.Accent;
        resources["ComboBoxBackgroundBorderBrushUnfocused"] = Palette.Border;
        resources["CalendarDatePickerTextForegroundDisabled"] = Palette.TextMuted;
        resources["CalendarViewCalendarItemForeground"] = Palette.Text;
        resources["CalendarViewCalendarItemRevealBackground"] = Palette.Surface;
        resources["CalendarViewCalendarItemRevealBorderBrush"] = Palette.Surface;
        resources["CalendarViewOutOfScopeBackground"] = Palette.Surface;
        resources["CalendarViewOutOfScopeForeground"] = Palette.TextMuted;
        resources["CalendarViewBlackoutForeground"] = Palette.TextMuted;
        resources["CalendarViewWeekDayForegroundDisabled"] = Palette.TextMuted;
        resources["CalendarViewTodayForeground"] = Palette.OnAccent;
        foreach (var state in (string[])["Hover", "Pressed", "Selected", "SelectedHover", "SelectedPressed"])
        {
            resources["CalendarView" + state + "BorderBrush"] = Palette.Accent;
        }
        resources["SystemControlErrorTextForegroundBrush"] = Palette.Error;
        resources["ToolTipBackground"] = Palette.PopupBackground;
        resources["ToolTipForeground"] = Palette.Text;
        resources["ToolTipBorderBrush"] = Palette.PopupBorder;
    }
}