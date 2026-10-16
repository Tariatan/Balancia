using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Balancia.Desktop.Localization;

namespace Balancia.Desktop;

public partial class MainWindow
{
    internal sealed record LanguageOption(string Code, string NativeName)
    {
        public override string ToString() => NativeName;
    }

    internal static readonly LanguageOption[] Languages =
    [
        new("en", "English"),
        new("de", "Deutsch"),
        new("ru", "Русский"),
        new("uk", "Українська")
    ];

    internal async Task ApplyLanguageChange(string code)
    {
        var updated = settings with { Language = code };
        PersistSettings(updated);
        settings = updated;
        LanguageCode = code;
        UiText.SetLanguage(code);
        Serilog.Log.Information("Application language changed, Language: '{Language}'", code);
        await Refresh();
    }

    private const double FlagWidth = 24;
    private const double FlagHeight = 16;

    internal static Canvas FlagIcon(string language)
    {
        var flag = new Canvas
        {
            Width = FlagWidth,
            Height = FlagHeight,
            ClipToBounds = true
        };
        switch (language)
        {
            case "de":
                Stripes("#000000", "#DD0000", "#FFCE00");
                break;
            case "ru":
                Stripes("#FFFFFF", "#0039A6", "#D52B1E");
                break;
            case "uk":
                Stripes("#0057B7", "#FFD700");
                break;
            default:
                Band("#FFFFFF", 0, FlagHeight);
                for (var index = 0; index < 7; index++)
                {
                    Band("#B22234", index * 2 * FlagHeight / 13, FlagHeight / 13);
                }

                var canton = new Rectangle
                {
                    Width = 10,
                    Height = 8.6,
                    Fill = Brush.Parse("#3C3B6E")
                };
                flag.Children.Add(canton);
                for (var row = 0; row < 3; row++)
                {
                    for (var column = 0; column < 4; column++)
                    {
                        var star = new Ellipse
                        {
                            Width = 1,
                            Height = 1,
                            Fill = Brushes.White
                        };
                        Canvas.SetLeft(star, 1.3 + column * 2.3);
                        Canvas.SetTop(star, 1.1 + row * 2.7);
                        flag.Children.Add(star);
                    }
                }

                break;
        }

        return flag;

        void Stripes(params string[] colors)
        {
            var height = FlagHeight / colors.Length;
            foreach (var (index, color) in colors.Index())
            {
                Band(color, index * height, height);
            }
        }

        void Band(string color, double top, double height)
        {
            var band = new Rectangle
            {
                Width = FlagWidth,
                Height = height,
                Fill = Brush.Parse(color)
            };
            Canvas.SetTop(band, top);
            flag.Children.Add(band);
        }
    }
}
