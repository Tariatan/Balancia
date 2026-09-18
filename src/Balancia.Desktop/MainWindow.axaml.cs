using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Balancia.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void ShowOverview(object? sender, RoutedEventArgs e) => Navigate(
        "Overview", "A clear view of your accounts and monthly activity.", null);

    private void ShowAccounts(object? sender, RoutedEventArgs e) => Navigate(
        "Accounts", "Balances across your accounts.",
        "Account setup is not available yet. You will be able to add UBS, Cash, and Revolut with their opening balances.");

    private void ShowTransactions(object? sender, RoutedEventArgs e) => Navigate(
        "Transactions", "Income, expenses, and transfers in one place.",
        "Transaction entry, search, and CSV import are not available yet. Your existing export has not been imported.");

    private void ShowRecurring(object? sender, RoutedEventArgs e) => Navigate(
        "Recurring payments", "Keep expected payments in view.",
        "Reminder setup is not available yet. You will be able to set an expected date and repeat interval for each payment.");

    private void Navigate(string title, string description, string? message)
    {
        PageTitle.Text = title;
        PageDescription.Text = description;
        OverviewPanel.IsVisible = message is null;
        SectionPanel.IsVisible = message is not null;
        SectionMessage.Text = message;
    }
}
