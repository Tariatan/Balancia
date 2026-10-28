using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>Builds the five-column transaction row shown in the transactions list and in the remove confirmation.</summary>
internal static class TransactionRows
{
    /// <summary>The row for a hit: date, signed amount (the account's effect when a single account is filtered), category, account and description.</summary>
    internal static Grid Build(TransactionsHit hit, string emptyDescriptionText = "")
    {
        var entry = hit.Entry;
        var effect = hit.AccountEffect;
        var amount = effect ?? entry.Draft.Amount;
        var sign = effect is { } accountEffect ? accountEffect < Money.Zero ? "− " : "+ " :
            entry.Draft.Kind switch
            {
                TransactionKind.Expense => "− ",
                TransactionKind.Income => "+ ",
                _ => "↔ "
            };

        return Build(entry.Draft.Date.ToString("dd MMM yyyy", Culture),
            string.IsNullOrWhiteSpace(entry.Draft.Description) ? emptyDescriptionText : entry.Draft.Description,
            entry.CategoryPath ?? "—",
            entry.DestinationName is null ? entry.AccountName : $"{entry.AccountName} → {entry.DestinationName}",
            sign + AmountText(amount.Abs()), entry.Draft.Kind);
    }

    private static Grid Build(string date, string description, string category, string account, string amount, TransactionKind kind)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,120,200,130,*"),
            MinHeight = 20
        };
        var values = new[] { date, amount, category, account, description };
        var amountBrush = kind switch
        {
            TransactionKind.Income => Palette.Positive,
            TransactionKind.Expense => Palette.Negative,
            _ => Palette.Transfer
        };

        for (var i = 0; i < values.Length; i++)
        {
            var isAmount = i == 1;
            var cell = new TextBlock
            {
                Text = values[i],
                FontSize = 13,
                FontWeight = isAmount ? FontWeight.Bold : FontWeight.Normal,
                Foreground = isAmount ? amountBrush : Palette.Text,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = isAmount ? TextAlignment.Right : TextAlignment.Left,
                Margin = new Thickness(0, 0, 30, 0)
            };
            GridLayout.Place(row, cell, i);
        }

        return row;
    }
}
