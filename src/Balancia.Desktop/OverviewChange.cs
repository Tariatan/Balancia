namespace Balancia.Desktop;

/// <summary>
/// What a change to <see cref="OverviewState"/> leaves for the window to do: reload the data and bring the controls that show the state
/// back in line. <see cref="OverviewState"/> decides this, so no caller has to remember which controls follow which action.
/// </summary>
[Flags]
internal enum OverviewChange
{
    /// <summary>Nothing changed.</summary>
    None = 0,

    /// <summary>The data the overview shows must be loaded again.</summary>
    Reload = 1,

    /// <summary>The period buttons must highlight the current period.</summary>
    PeriodButtons = 2,

    /// <summary>The trend zoom changed, so its label must be shown again.</summary>
    TrendInterval = 4,

    /// <summary>The from and to dates must be shown again.</summary>
    Dates = 8,

    /// <summary>The description and amount inputs must be shown again.</summary>
    Inputs = 16,

    /// <summary>The account list's filter checkboxes must be shown again.</summary>
    AccountChecks = 32,

    /// <summary>The category list's filter checkboxes must be shown again.</summary>
    CategoryChecks = 64,
}
