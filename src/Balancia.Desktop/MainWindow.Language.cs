using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Balancia.Desktop.Localization;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private sealed record LanguageOption(string Code, string NativeName)
    {
        public override string ToString() => NativeName;
    }

    private static readonly LanguageOption[] Languages =
    [
        new("en", "English"),
        new("de", "Deutsch"),
        new("ru", "Русский"),
        new("uk", "Українська"),
    ];

    private ComboBox LanguagePicker(Window dialog)
    {
        var picker = new ComboBox
        {
            ItemsSource = Languages,
            SelectedItem = Languages.Single(option => option.Code == languageCode),
            ItemTemplate = new FuncDataTemplate<LanguageOption>((option, _) =>
            {
                return
                    // Virtualized presenters can request a template with no item while scrolling.
                    option is null
                    ? null
                    : new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 9,
                        Children =
                        {
                            FlagIcon(option.Code),
                            new TextBlock
                            {
                                Text = option.NativeName, VerticalAlignment = VerticalAlignment.Center,
                            },
                        },
                    };
            }),
            Width = 170,
        };
        picker.SelectionChanged += async (_, _) =>
        {
            if (picker.SelectedItem is not LanguageOption selected || selected.Code == languageCode)
            {
                return;
            }

            try
            {
                SaveApplicationSettings(databasePath, backupPath, snapshotPath, defaultAccountId, selected.Code);
                languagePreference = selected.Code;
                languageCode = selected.Code;
                UiText.SetLanguage(languageCode);
                dialog.Close();
                await Refresh();
                Dispatcher.UIThread.Post(() => _ = OpenSettingsDialog());
            }
            catch (Exception ex)
            {
                picker.SelectedItem = Languages.Single(option => option.Code == languageCode);
                await ShowErrorDialog("Balancia", FriendlyError(ex));
            }
        };
        return picker;
    }

    private static Canvas FlagIcon(string language)
    {
        var flag = new Canvas
        {
            Width = 24,
            Height = 16,
            ClipToBounds = true,
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
                    Fill = Brush.Parse("#3C3B6E"),
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
                            Fill = Brushes.White,
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
                Fill = Brush.Parse(color),
            };
            Canvas.SetTop(band, top);
            flag.Children.Add(band);
        }
    }
}
