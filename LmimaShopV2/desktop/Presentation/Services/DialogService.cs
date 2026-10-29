using System.Windows;

namespace LmimaShopV2.Desktop.Presentation.Services;

public interface IDialogService
{
    void ShowInfo(string message, string title = "LmimaShop");
    void ShowError(string message, string title = "Erreur");
    bool Confirm(string message, string title = "Confirmation");
}

public sealed class DialogService : IDialogService
{
    public void ShowInfo(string message, string title = "LmimaShop") =>
        Show(message, title, MessageBoxImage.Information);

    public void ShowError(string message, string title = "Erreur") =>
        Show(message, title, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "Confirmation")
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
        var result = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    private static void Show(string message, string title, MessageBoxImage icon)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
        if (owner is null)
            MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        else
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, icon);
    }
}
