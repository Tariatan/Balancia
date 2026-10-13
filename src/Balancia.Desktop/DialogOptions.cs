using Avalonia.Input;

namespace Balancia.Desktop;

internal enum DialogSize
{
    /// <summary>Short confirmation prompt.</summary>
    Compact,

    /// <summary>Confirmation prompt that shows a full-width row, such as a transaction.</summary>
    Wide,

    /// <summary>Regular form dialog.</summary>
    Standard,

    /// <summary>Form dialog with many fields.</summary>
    Tall
}

internal sealed record DialogOptions(
    DialogSize Size = DialogSize.Standard,
    string SaveLabel = "Save",
    InputElement? InitialFocus = null,
    Func<Task>? OnSaveAndContinue = null,
    Func<string?>? Validate = null)
{
    /// <summary>Confirmation dialogs never make the destructive button the Enter default.</summary>
    public bool IsConfirmation => Size is DialogSize.Compact or DialogSize.Wide;

    public static DialogOptions Confirmation(string saveLabel, Func<string?>? validate = null, DialogSize size = DialogSize.Compact) =>
        new(size, saveLabel, Validate: validate);
}
