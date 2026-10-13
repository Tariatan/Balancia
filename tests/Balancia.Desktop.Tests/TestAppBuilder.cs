using Avalonia;
using Avalonia.Headless;
using Balancia.Desktop.Tests;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Balancia.Desktop.Tests;

/// <summary>Starts the real <see cref="App"/> (theme and styles) on Avalonia's headless platform. No main window is created.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
