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
        SaveApplicationSettings(databasePath, backupPath, snapshotPath, defaultAccountId, code);
        languagePreference = code;
        languageCode = code;
        UiText.SetLanguage(code);
        await Refresh();
    }

    internal static Canvas FlagIcon(string language)
    {
        var flag = new Canvas
        {
            Width = 24,
            Height = 16,
            ClipToBounds = true
        };
        switch (language)
        {
            case "de":
                Band("#000000", 0, 16d / 3);
                Band("#DD0000", 16d / 3, 16d / 3);
                Band("#FFCE00", 32d / 3, 16d / 3);
                break;
            case "ru":
                Band("#FFFFFF", 0, 16d / 3);
                Band("#0039A6", 16d / 3, 16d / 3);
                Band("#D52B1E", 32d / 3, 16d / 3);
                break;
            case "uk":
                Band("#0057B7", 0, 8);
                Band("#FFD700", 8, 8);
                break;
            default:
                Band("#FFFFFF", 0, 16);
                for (var index = 0; index < 7; index++)
                {
                    Band("#B22234", index * 32d / 13, 16d / 13);
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

        void Band(string color, double top, double height)
        {
            var band = new Rectangle
            {
                Width = 24,
                Height = height,
                Fill = Brush.Parse(color)
            };
            Canvas.SetTop(band, top);
            flag.Children.Add(band);
        }
    }
}
