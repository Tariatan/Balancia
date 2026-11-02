using Android.Content;

namespace Balancia.Android;

public partial class MainActivity
{
    private bool lightTheme;

    private void ApplyTheme()
    {
        SetTheme(lightTheme ? Resource.Style.ViewerLight : Resource.Style.ViewerDark);
        ViewerPalette.Apply(lightTheme);
    }

    private void ToggleTheme()
    {
        if (importing)
        {
            return;
        }

        var statusText = status?.Text;
        descriptionQuery = search?.Text ?? descriptionQuery;
        search = null;
        lightTheme = !lightTheme;
        using var preferences = GetSharedPreferences("viewer", FileCreationMode.Private)!;
        using var editor = preferences.Edit()!;
        editor.PutBoolean("light-theme", lightTheme);
        editor.Apply();
        ApplyTheme();
        BuildDashboard();
        status!.Text = statusText;
    }
}
