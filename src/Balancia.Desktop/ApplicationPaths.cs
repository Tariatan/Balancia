namespace Balancia.Desktop;

internal static class ApplicationPaths
{
    public static string UserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Balancia");

    public static string SettingsFile => Path.Combine(UserDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(UserDirectory, "log");
}
