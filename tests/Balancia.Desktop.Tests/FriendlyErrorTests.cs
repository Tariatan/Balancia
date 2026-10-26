using Microsoft.Data.Sqlite;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class FriendlyErrorTests
{
    public FriendlyErrorTests() => Localization.UiText.SetLanguage("en");

    [Fact]
    public void FriendlyError_ConstraintViolation_ExplainsTheConflict()
    {
        // Arrange
        var error = new SqliteException("UNIQUE constraint failed: private detail", 19);

        // Act
        var message = MainWindow.FriendlyError(error);

        // Assert
        Assert.Equal("This change conflicts with existing data. Check names and referenced accounts/categories.", message);
    }

    [Theory]
    [InlineData(5)] // busy
    [InlineData(6)] // locked
    [InlineData(10)] // I/O error
    [InlineData(11)] // corrupt
    [InlineData(26)] // not a database
    public void FriendlyError_OtherDatabaseFailure_AsksToCloseOtherWindowsAndRetry(int sqliteCode)
    {
        // Arrange
        var error = new SqliteException("private detail", sqliteCode);

        // Act
        var message = MainWindow.FriendlyError(error);

        // Assert
        Assert.Equal("The database could not be read or saved. Close other Balancia windows and try again.", message);
    }

    [Fact]
    public void FriendlyError_ArgumentException_ShowsOnlyTheFirstMessageLine()
    {
        // Arrange
        var error = new ArgumentException("Enter a description.\r\nsecond line");

        // Act
        var message = MainWindow.FriendlyError(error);

        // Assert
        Assert.Equal("Enter a description.", message);
    }

    [Fact]
    public void FriendlyError_IoFailure_ReportsTheDataFolder()
    {
        // Act
        var message = MainWindow.FriendlyError(new IOException("private path"));

        // Assert
        Assert.Equal("The local data folder is unavailable or not writable.", message);
    }

    [Fact]
    public void FriendlyError_UnknownFailure_KeepsTheEnteredValuesMessage()
    {
        // Act
        var message = MainWindow.FriendlyError(new NotSupportedException("private detail"));

        // Assert
        Assert.Equal("The operation failed. Your entered values have been kept; try again.", message);
    }
}
